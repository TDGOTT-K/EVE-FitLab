using EdenOS.Application.RuntimeState;
using EdenOS.Contracts.Fitting;
using EdenOS.Contracts.UseCases;

namespace EdenOS.Application.Fitting;

public sealed class InMemoryFitEnvironmentProfileService : IFitEnvironmentProfileService
{
    private const int PersistenceRetryCount = 3;

    private readonly TimeProvider timeProvider;
    private readonly LocalJsonStateStore<FittingRuntimeState>? stateStore;
    private readonly object syncRoot = new();
    private Dictionary<string, FitEnvironmentProfile> profiles;

    public InMemoryFitEnvironmentProfileService(TimeProvider timeProvider)
    {
        this.timeProvider = timeProvider;
        profiles = new Dictionary<string, FitEnvironmentProfile>(StringComparer.OrdinalIgnoreCase);
    }

    internal InMemoryFitEnvironmentProfileService(
        TimeProvider timeProvider,
        LocalJsonStateStore<FittingRuntimeState> stateStore)
        : this(timeProvider)
    {
        this.stateStore = stateStore;
        LoadPersistedState();
    }

    public UseCaseResult<FitEnvironmentProfile> SaveProfile(SaveFitEnvironmentProfileRequest request)
    {
        var traceId = CreateTraceId("fit.environment.save_profile");
        var errors = ValidateProfileShape(request.Profile);
        if (errors.Count > 0)
        {
            return UseCaseResult<FitEnvironmentProfile>.Failure(
                UseCaseStatus.InvalidInput,
                "Fit environment profile is invalid.",
                traceId,
                errors);
        }

        lock (syncRoot)
        {
            var saveResult = TrySaveProfile(request);
            if (saveResult.Conflict)
            {
                return UseCaseResult<FitEnvironmentProfile>.Failure(
                    UseCaseStatus.Conflict,
                    $"Fit environment profile '{saveResult.ProfileId}' already exists.",
                    traceId,
                    [$"profile '{saveResult.ProfileId}' already exists."]);
            }

            if (saveResult.Profile is null)
            {
                return UseCaseResult<FitEnvironmentProfile>.Failure(
                    UseCaseStatus.Conflict,
                    "Saving fit environment profile detected repeated concurrent runtime state updates.",
                    traceId,
                    ["Shared fitting runtime state changed repeatedly while this request was executing. Retry the request."]);
            }

            return UseCaseResult<FitEnvironmentProfile>.Success(
                saveResult.Profile,
                $"Saved fit environment profile '{saveResult.Profile.ProfileId}'.",
                traceId);
        }
    }

    public UseCaseResult<FitEnvironmentProfile> GetProfile(GetFitEnvironmentProfileRequest request)
    {
        var traceId = CreateTraceId("fit.environment.get_profile");
        var profileId = NormalizeRequiredId(request.ProfileId);
        if (profileId is null)
        {
            return UseCaseResult<FitEnvironmentProfile>.Failure(
                UseCaseStatus.InvalidInput,
                "Profile id is required.",
                traceId,
                ["profile_id is required."]);
        }

        lock (syncRoot)
        {
            return profiles.TryGetValue(profileId, out var profile)
                ? UseCaseResult<FitEnvironmentProfile>.Success(
                    profile,
                    $"Loaded fit environment profile '{profile.ProfileId}'.",
                    traceId)
                : UseCaseResult<FitEnvironmentProfile>.Failure(
                    UseCaseStatus.NotFound,
                    $"Fit environment profile '{profileId}' was not found.",
                    traceId,
                    [$"profile '{profileId}' was not found."]);
        }
    }

    public UseCaseResult<FitEnvironmentProfileCatalogView> ListProfiles(ListFitEnvironmentProfilesRequest request)
    {
        var traceId = CreateTraceId("fit.environment.list_profiles");
        lock (syncRoot)
        {
            var summaries = profiles.Values
                .Where(profile => string.IsNullOrWhiteSpace(request.WorkspaceId) ||
                    string.Equals(profile.WorkspaceId, request.WorkspaceId.Trim(), StringComparison.OrdinalIgnoreCase))
                .Where(profile => string.IsNullOrWhiteSpace(request.CharacterId) ||
                    string.Equals(profile.CharacterId, request.CharacterId.Trim(), StringComparison.OrdinalIgnoreCase))
                .OrderBy(profile => profile.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(profile => profile.ProfileId, StringComparer.OrdinalIgnoreCase)
                .Select(ToSummary)
                .ToArray();

            return UseCaseResult<FitEnvironmentProfileCatalogView>.Success(
                new FitEnvironmentProfileCatalogView
                {
                    Profiles = summaries
                },
                $"Listed {summaries.Length} fit environment profile(s).",
                traceId);
        }
    }

    public UseCaseResult<FitEnvironmentProfileDeleteResult> DeleteProfile(DeleteFitEnvironmentProfileRequest request)
    {
        var traceId = CreateTraceId("fit.environment.delete_profile");
        var profileId = NormalizeRequiredId(request.ProfileId);
        if (profileId is null)
        {
            return UseCaseResult<FitEnvironmentProfileDeleteResult>.Failure(
                UseCaseStatus.InvalidInput,
                "Profile id is required.",
                traceId,
                ["profile_id is required."]);
        }

        lock (syncRoot)
        {
            var deleteResult = TryDeleteProfile(profileId);
            if (!deleteResult.Found)
            {
                return UseCaseResult<FitEnvironmentProfileDeleteResult>.Failure(
                    UseCaseStatus.NotFound,
                    $"Fit environment profile '{profileId}' was not found.",
                    traceId,
                    [$"profile '{profileId}' was not found."]);
            }

            if (!deleteResult.Persisted)
            {
                return UseCaseResult<FitEnvironmentProfileDeleteResult>.Failure(
                    UseCaseStatus.Conflict,
                    "Deleting fit environment profile detected repeated concurrent runtime state updates.",
                    traceId,
                    ["Shared fitting runtime state changed repeatedly while this request was executing. Retry the request."]);
            }

            return UseCaseResult<FitEnvironmentProfileDeleteResult>.Success(
                new FitEnvironmentProfileDeleteResult
                {
                    ProfileId = profileId,
                    Deleted = true
                },
                $"Deleted fit environment profile '{profileId}'.",
                traceId);
        }
    }

    public UseCaseResult<FitEnvironmentProfileApplyResult> ApplyProfile(ApplyFitEnvironmentProfileRequest request)
    {
        var traceId = CreateTraceId("fit.environment.apply_profile");
        var profileId = NormalizeRequiredId(request.ProfileId);
        if (profileId is null)
        {
            return UseCaseResult<FitEnvironmentProfileApplyResult>.Failure(
                UseCaseStatus.InvalidInput,
                "Profile id is required.",
                traceId,
                ["profile_id is required."]);
        }

        lock (syncRoot)
        {
            if (!profiles.TryGetValue(profileId, out var profile))
            {
                return UseCaseResult<FitEnvironmentProfileApplyResult>.Failure(
                    UseCaseStatus.NotFound,
                    $"Fit environment profile '{profileId}' was not found.",
                    traceId,
                    [$"profile '{profileId}' was not found."]);
            }

            var snapshot = request.Snapshot with
            {
                Skills = request.ReplaceSkills
                    ? profile.Skills
                    : MergeSkills(request.Snapshot.Skills, profile.Skills),
                Implants = request.ReplaceImplants
                    ? profile.Implants
                    : MergeImplants(request.Snapshot.Implants, profile.Implants),
                Boosters = request.ReplaceBoosters
                    ? profile.Boosters
                    : MergeBoosters(request.Snapshot.Boosters, profile.Boosters)
            };

            var result = new FitEnvironmentProfileApplyResult
            {
                ProfileId = profile.ProfileId,
                Profile = profile,
                Snapshot = snapshot,
                AppliedImplantCount = profile.Implants.Count,
                AppliedBoosterCount = profile.Boosters.Count,
                AppliedSkillCount = profile.Skills.Count
            };

            return UseCaseResult<FitEnvironmentProfileApplyResult>.Success(
                result,
                $"Applied fit environment profile '{profile.ProfileId}' to fit '{snapshot.FitId}'.",
                traceId);
        }
    }

    private SaveProfileResult TrySaveProfile(SaveFitEnvironmentProfileRequest request)
    {
        var profileId = request.Profile.ProfileId.Trim();
        for (var attempt = 0; attempt < PersistenceRetryCount; attempt++)
        {
            if (stateStore is not null)
            {
                LoadPersistedState();
            }

            if (!request.Overwrite && profiles.ContainsKey(profileId))
            {
                return new SaveProfileResult(profileId, Profile: null, Conflict: true);
            }

            var now = timeProvider.GetUtcNow();
            var createdAt = profiles.TryGetValue(profileId, out var existing)
                ? existing.CreatedAtUtc
                : NormalizeTimestamp(request.Profile.CreatedAtUtc, now);
            var normalized = NormalizeProfile(request.Profile, profileId, createdAt, now);
            profiles[profileId] = normalized;

            if (TryPersistCurrentState(attempt))
            {
                return new SaveProfileResult(profileId, normalized, Conflict: false);
            }
        }

        return new SaveProfileResult(profileId, Profile: null, Conflict: false);
    }

    private DeleteProfileResult TryDeleteProfile(string profileId)
    {
        for (var attempt = 0; attempt < PersistenceRetryCount; attempt++)
        {
            if (stateStore is not null)
            {
                LoadPersistedState();
            }

            if (!profiles.Remove(profileId))
            {
                return new DeleteProfileResult(Found: false, Persisted: false);
            }

            if (TryPersistCurrentState(attempt))
            {
                return new DeleteProfileResult(Found: true, Persisted: true);
            }
        }

        return new DeleteProfileResult(Found: true, Persisted: false);
    }

    private void LoadPersistedState()
    {
        if (stateStore is null)
        {
            return;
        }

        var state = stateStore.Load();
        profiles = state.EnvironmentProfiles
            .Where(profile => !string.IsNullOrWhiteSpace(profile.ProfileId))
            .GroupBy(profile => profile.ProfileId.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.OrdinalIgnoreCase);
    }

    private bool TryPersistCurrentState(int attempt)
    {
        if (stateStore is null)
        {
            return true;
        }

        try
        {
            stateStore.Save(new FittingRuntimeState
            {
                EnvironmentProfiles = profiles.Values
                    .OrderBy(profile => profile.ProfileId, StringComparer.OrdinalIgnoreCase)
                    .ToArray()
            });
            return true;
        }
        catch (RuntimeStateConcurrencyException) when (attempt < PersistenceRetryCount - 1)
        {
            return false;
        }
    }

    private static FitEnvironmentProfile NormalizeProfile(
        FitEnvironmentProfile profile,
        string profileId,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt)
    {
        return profile with
        {
            ProfileId = profileId,
            Name = profile.Name.Trim(),
            WorkspaceId = NormalizeOptional(profile.WorkspaceId),
            CharacterId = NormalizeOptional(profile.CharacterId),
            CharacterName = NormalizeOptional(profile.CharacterName),
            Skills = profile.Skills
                .Select(NormalizeSkill)
                .OrderBy(skill => skill.SkillTypeId)
                .ToArray(),
            Implants = profile.Implants
                .Select(NormalizeImplant)
                .ToArray(),
            Boosters = profile.Boosters
                .Select(NormalizeBooster)
                .ToArray(),
            CreatedAtUtc = createdAt,
            UpdatedAtUtc = updatedAt,
            Notes = NormalizeOptional(profile.Notes)
        };
    }

    private static FitSkillLevel NormalizeSkill(FitSkillLevel skill)
    {
        return skill with
        {
            Level = Math.Clamp(skill.Level, 0, 5)
        };
    }

    private static FitImplant NormalizeImplant(FitImplant implant)
    {
        return implant with
        {
            TypeId = implant.TypeId.Trim(),
            Name = implant.Name.Trim()
        };
    }

    private static FitBooster NormalizeBooster(FitBooster booster)
    {
        return booster with
        {
            TypeId = booster.TypeId.Trim(),
            Name = booster.Name.Trim(),
            SideEffects = booster.SideEffects
                .OrderBy(effect => effect.EffectId)
                .ToArray()
        };
    }

    private static FitEnvironmentProfileSummary ToSummary(FitEnvironmentProfile profile)
    {
        return new FitEnvironmentProfileSummary
        {
            ProfileId = profile.ProfileId,
            Name = profile.Name,
            WorkspaceId = profile.WorkspaceId,
            CharacterId = profile.CharacterId,
            CharacterName = profile.CharacterName,
            SkillCount = profile.Skills.Count,
            ImplantCount = profile.Implants.Count,
            BoosterCount = profile.Boosters.Count,
            CreatedAtUtc = profile.CreatedAtUtc,
            UpdatedAtUtc = profile.UpdatedAtUtc
        };
    }

    private static IReadOnlyList<FitSkillLevel> MergeSkills(
        IReadOnlyList<FitSkillLevel> existing,
        IReadOnlyList<FitSkillLevel> incoming)
    {
        return existing
            .Concat(incoming)
            .GroupBy(skill => skill.SkillTypeId)
            .Select(group => group.Last())
            .OrderBy(skill => skill.SkillTypeId)
            .ToArray();
    }

    private static IReadOnlyList<FitImplant> MergeImplants(
        IReadOnlyList<FitImplant> existing,
        IReadOnlyList<FitImplant> incoming)
    {
        return existing
            .Concat(incoming)
            .GroupBy(implant => implant.SlotIndex?.ToString() ?? implant.TypeId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last())
            .ToArray();
    }

    private static IReadOnlyList<FitBooster> MergeBoosters(
        IReadOnlyList<FitBooster> existing,
        IReadOnlyList<FitBooster> incoming)
    {
        return existing
            .Concat(incoming)
            .GroupBy(booster => booster.BoosterSlot?.ToString() ?? booster.TypeId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last())
            .ToArray();
    }

    private static List<string> ValidateProfileShape(FitEnvironmentProfile profile)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(profile.ProfileId))
        {
            errors.Add("profile.profile_id is required.");
        }

        if (string.IsNullOrWhiteSpace(profile.Name))
        {
            errors.Add("profile.name is required.");
        }

        foreach (var skill in profile.Skills)
        {
            if (skill.SkillTypeId <= 0)
            {
                errors.Add("skill.skill_type_id must be greater than zero.");
            }

            if (skill.Level is < 0 or > 5)
            {
                errors.Add("skill.level must stay between 0 and 5.");
            }
        }

        foreach (var implant in profile.Implants)
        {
            if (string.IsNullOrWhiteSpace(implant.TypeId))
            {
                errors.Add("implant.type_id is required.");
            }

            if (string.IsNullOrWhiteSpace(implant.Name))
            {
                errors.Add("implant.name is required.");
            }
        }

        foreach (var booster in profile.Boosters)
        {
            if (string.IsNullOrWhiteSpace(booster.TypeId))
            {
                errors.Add("booster.type_id is required.");
            }

            if (string.IsNullOrWhiteSpace(booster.Name))
            {
                errors.Add("booster.name is required.");
            }
        }

        return errors;
    }

    private static string? NormalizeRequiredId(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static DateTimeOffset NormalizeTimestamp(DateTimeOffset timestamp, DateTimeOffset fallback)
    {
        return timestamp == default ? fallback : timestamp;
    }

    private static string CreateTraceId(string operation) => $"{operation}:{Guid.NewGuid():N}";

    private sealed record SaveProfileResult(string ProfileId, FitEnvironmentProfile? Profile, bool Conflict);

    private sealed record DeleteProfileResult(bool Found, bool Persisted);
}
