from pathlib import Path
r=Path(r'D:/IT/EVE/EdenOsRewrite')
p=r/'src/contracts/EdenOS.Contracts/Fitting/FittingContracts.cs';s=p.read_text(encoding='utf-8').replace('public sealed record FittedModule\n{','public sealed record FittedModule\n{\n    public IReadOnlyDictionary<string, FitAttributeExecutionTrace> AttributeTraces { get; init; } = new Dictionary<string, FitAttributeExecutionTrace>();');p.write_text(s,encoding='utf-8')
p=r/'src/application-csharp/EdenOS.Application/Fitting/Dogma/Engine/FittingCalculator.cs';s=p.read_text(encoding='utf-8');a=s.index('    internal IReadOnlyList<FittingModuleCombatProjection> ProjectCombatModules');b=s.index('    private static IReadOnlyList<FittingAttributeModifierTrace>',a);part=s[a:b];part=part.replace('var effectCache = new Dictionary<(TargetObjectKind Kind, int RefIndex, int TypeId, int AttributeId), double>();','var effectCache = new TracedAttributeCache();');part=part.replace('        return projections;', '''        for (var i = 0; i < projections.Count; i++)
            projections[i] = projections[i] with { AttributeTraces = effectCache.Traces
                .Where(e => e.Key.Kind == TargetObjectKind.Item && e.Key.RefIndex == projections[i].ModuleIndex)
                .ToDictionary(e => e.Value.Attribute, e => e.Value) };
        return projections;''');s=s[:a]+part+s[b:]
s=s.replace('internal sealed record FittingModuleCombatProjection\n{','internal sealed record FittingModuleCombatProjection\n{\n    public IReadOnlyDictionary<string, FitAttributeExecutionTrace> AttributeTraces { get; init; } = new Dictionary<string, FitAttributeExecutionTrace>();')
a=s.index('    private static double GetEffectiveTypeAttributeValueById(');b=s.index('    private static List<PendingEffect> CollectEffectsForTarget',a);part=s[a:b];part=part.replace('        if (effects.Count == 0)','''        var trace = effectCache is TracedAttributeCache ? new FitAttributeExecutionTrace
            { Attribute = meta?.Name ?? targetAttributeId.ToString(), BaseValue = baseValue, FinalValue = baseValue } : null;
        if (trace is not null) ((TracedAttributeCache)effectCache).Traces[cacheKey] = trace;
        if (effects.Count == 0)''');part=part.replace('ApplyEffects(baseValue, effects, meta?.HighIsGood ?? false, meta?.Stackable ?? false);','ApplyEffects(baseValue, effects, meta?.HighIsGood ?? false, meta?.Stackable ?? false, trace?.Steps);\n        if (trace is not null) trace.FinalValue = value;');s=s[:a]+part+s[b:]
a=s.index('    private static List<PendingEffect> CollectEffectsForTarget');b=s.index('    private static',a+30);part=s[a:b];part=part.replace('candidate.SourceCategoryId));','candidate.SourceCategoryId, CaptureModifierSource(candidate, ctx)));');s=s[:a]+part+s[b:]
pos=s.index('    private sealed record PendingEffect(');s=s[:pos]+'''    private sealed class TracedAttributeCache : Dictionary<(TargetObjectKind Kind, int RefIndex, int TypeId, int AttributeId), double>
    {
        public Dictionary<(TargetObjectKind Kind, int RefIndex, int TypeId, int AttributeId), FitAttributeExecutionTrace> Traces { get; } = new();
    }
    private static FitModifierExecutionStep CaptureModifierSource(TargetModifierCandidate candidate, DogmaContext ctx) => new()
    {
        SourceTypeId=candidate.Source.TypeId, SourceKind=candidate.Source.Kind.ToString(), SourceIndex=candidate.Source.RefIndex,
        SourceState=candidate.Source.State.ToString(), SkillLevel=candidate.Source.SkillLevel,
        EffectId=candidate.EffectId, ModifyingAttributeId=candidate.ModifyingAttributeId,
        SourceBaseValue=GetTypeAttributeValue(candidate.Source.TypeId,candidate.ModifyingAttributeId,ctx)
    };
'''+s[pos:];s=s.replace('        int SourceCategoryId);\n\n    private sealed class TracedAttributeCache','        int SourceCategoryId, int EffectId = 0);\n\n    private sealed class TracedAttributeCache');
a=s.index('list.Add(new TargetModifierCandidate(');b=s.index('));',a)+3;part=s[a:b].replace('SourceCategoryId: sourceCategoryId','SourceCategoryId: sourceCategoryId, EffectId: typeEffect.EffectId');s=s[:a]+part+s[b:];p.write_text(s,encoding='utf-8')
p=r/'src/application-csharp/EdenOS.Application/Fitting/Dogma/DogmaFitAdapter.cs';s=p.read_text(encoding='utf-8').replace('                    CpuUsage = projection.CpuUsage,','                    AttributeTraces = projection.AttributeTraces,\n                    CpuUsage = projection.CpuUsage,');p.write_text(s,encoding='utf-8')
