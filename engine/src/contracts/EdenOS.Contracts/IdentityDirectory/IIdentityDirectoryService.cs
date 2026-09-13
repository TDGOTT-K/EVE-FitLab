using EdenOS.Contracts.UseCases;

namespace EdenOS.Contracts.IdentityDirectory;

public interface IIdentityDirectoryService
{
    UseCaseResult<IdentityDirectoryResolvedSubject> ResolveSubject(ResolveIdentityDirectorySubjectRequest request);

    UseCaseResult<IdentityDirectoryUserSummary> GetUser(GetIdentityDirectoryUserRequest request);

    UseCaseResult<IdentityDirectoryUserSummary> GetUserBySubject(GetIdentityDirectoryUserBySubjectRequest request);

    UseCaseResult<IReadOnlyList<IdentityDirectoryIdentitySummary>> ListIdentitiesByUser(ListIdentityDirectoryIdentitiesByUserRequest request);

    UseCaseResult<IdentityDirectoryIdentitySummary> GetIdentity(GetIdentityDirectoryIdentityRequest request);

    UseCaseResult<IReadOnlyList<IdentityDirectoryActorSummary>> ListActorsByUser(ListIdentityDirectoryActorsByUserRequest request);

    UseCaseResult<IdentityDirectoryActorSummary> GetActor(GetIdentityDirectoryActorRequest request);

    UseCaseResult<IReadOnlyList<IdentityDirectoryActorSummary>> ListSwitchableActors(ListSwitchableIdentityDirectoryActorsRequest request);

    UseCaseResult<IdentityDirectoryOrganizationSummary> GetOrganization(GetIdentityDirectoryOrganizationRequest request);

    UseCaseResult<IReadOnlyList<IdentityDirectoryOrganizationSummary>> ListOrganizationsByUser(ListIdentityDirectoryOrganizationsByUserRequest request);

    UseCaseResult<IReadOnlyList<IdentityDirectoryMembershipSummary>> ListMembershipsByUser(ListIdentityDirectoryMembershipsByUserRequest request);

    UseCaseResult<IReadOnlyList<IdentityDirectoryMembershipSummary>> ListMembershipsByOrganization(ListIdentityDirectoryMembershipsByOrganizationRequest request);

    UseCaseResult<IReadOnlyList<IdentityDirectoryAccessGrantSummary>> ListAccessGrantsForSubject(ListIdentityDirectoryAccessGrantsForSubjectRequest request);

    UseCaseResult<IdentityDirectoryAccessDecision> CheckAccess(CheckIdentityDirectoryAccessRequest request);

    UseCaseResult<IdentityDirectoryUserSummary> ProvisionUserFromSubject(ProvisionIdentityDirectoryUserFromSubjectRequest request);

    UseCaseResult<IdentityDirectoryUserSummary> DisableUser(DisableIdentityDirectoryUserRequest request);

    UseCaseResult<IdentityDirectoryUserSummary> UpdateUserProfile(UpdateIdentityDirectoryUserProfileRequest request);

    UseCaseResult<IdentityDirectoryIdentitySummary> AttachExternalIdentity(AttachExternalIdentityRequest request);

    UseCaseResult<IdentityDirectoryIdentitySummary> CreateVirtualIdentity(CreateVirtualIdentityRequest request);

    UseCaseResult<IdentityDirectoryIdentitySummary> RevokeIdentity(RevokeIdentityRequest request);

    UseCaseResult<IdentityDirectoryActorSummary> CreateActor(CreateIdentityDirectoryActorRequest request);

    UseCaseResult<IdentityDirectoryActorSummary> UpdateActor(UpdateIdentityDirectoryActorRequest request);

    UseCaseResult<IdentityDirectoryActorSummary> DisableActor(DisableIdentityDirectoryActorRequest request);

    UseCaseResult<IdentityDirectoryActorSummary> SetDefaultActor(SetDefaultIdentityDirectoryActorRequest request);

    UseCaseResult<IdentityDirectoryActorSummary> SwitchActiveActor(SwitchActiveIdentityDirectoryActorRequest request);

    UseCaseResult<IdentityDirectoryOrganizationSummary> CreateOrganization(CreateIdentityDirectoryOrganizationRequest request);

    UseCaseResult<IdentityDirectoryMembershipSummary> AddMembership(AddIdentityDirectoryMembershipRequest request);

    UseCaseResult<IdentityDirectoryMembershipSummary> UpdateMembershipRole(UpdateIdentityDirectoryMembershipRoleRequest request);

    UseCaseResult<IdentityDirectoryMembershipSummary> SuspendMembership(SuspendIdentityDirectoryMembershipRequest request);

    UseCaseResult<IdentityDirectoryMembershipSummary> RevokeMembership(RevokeIdentityDirectoryMembershipRequest request);

    UseCaseResult<IdentityDirectoryAccessGrantSummary> AddAccessGrant(AddIdentityDirectoryAccessGrantRequest request);

    UseCaseResult<IdentityDirectoryAccessGrantSummary> RevokeAccessGrant(RevokeIdentityDirectoryAccessGrantRequest request);

    UseCaseResult<IReadOnlyList<IdentityDirectoryAccessGrantSummary>> RebuildAccessProjection(RebuildIdentityDirectoryAccessProjectionRequest request);

    UseCaseResult<IdentityDirectoryUserSessionView> BootstrapUserSession(BootstrapIdentityDirectoryUserSessionRequest request);

    UseCaseResult<IdentityDirectoryUserSessionView> CompleteEsiBinding(CompleteEsiIdentityBindingRequest request);

    UseCaseResult<IdentityDirectoryProtectedActorProvision> ProvisionProtectedInformantActor(ProvisionProtectedInformantActorRequest request);

    UseCaseResult<IdentityDirectoryActorSummary> ActivateMembershipActor(ActivateIdentityDirectoryMembershipActorRequest request);
}
