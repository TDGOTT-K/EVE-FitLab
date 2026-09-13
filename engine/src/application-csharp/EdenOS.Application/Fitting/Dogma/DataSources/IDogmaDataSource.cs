using EdenOS.Application.Fitting.Dogma.Sde;
using EdenOS.Application.Fitting.Dogma.Rules;

namespace EdenOS.Application.Fitting.Dogma.DataSources;

public interface IDogmaDataSource
{
    string CacheKey { get; }

    int BuildNumber { get; }

    IReadOnlyDictionary<string, int> AttributeNameToId { get; }

    IReadOnlyDictionary<string, int> EffectNameToId { get; }

    DogmaRuleSet RuleSet { get; }

    SdeTypeDogma? GetTypeDogma(int typeId);

    SdeType? GetType(int typeId);

    int? TryResolveTypeIdByName(string typeName);

    SdeGroup? GetGroup(int groupId);

    SdeDogmaAttribute? GetAttribute(int attributeId);

    SdeDogmaEffect? GetEffect(int effectId);
}
