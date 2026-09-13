from pathlib import Path
root=Path(r'D:/IT/EVE/EdenOsRewrite')
p=root/'src/contracts/EdenOS.Contracts/Fitting/FittingContracts.cs';s=p.read_text(encoding='utf-8');s=s.replace('    public IReadOnlyDictionary<string, decimal> AttributeSnapshot {', '    public IReadOnlyDictionary<string, FitAttributeExecutionTrace> AttributeTraces { get; init; } = new Dictionary<string, FitAttributeExecutionTrace>();\n\n    public IReadOnlyDictionary<string, decimal> AttributeSnapshot {');s+='''

public sealed record FitAttributeExecutionTrace
{
    public string Attribute { get; init; } = "";
    public double BaseValue { get; init; }
    public double FinalValue { get; set; }
    public bool IsComplete { get; set; } = true;
    public List<FitModifierExecutionStep> Steps { get; init; } = new();
}
public sealed record FitModifierExecutionStep
{
    public int Order { get; init; }
    public int SourceTypeId { get; init; }
    public string SourceKind { get; init; } = "";
    public int SourceIndex { get; init; }
    public string SourceState { get; init; } = "";
    public int SkillLevel { get; init; }
    public int EffectId { get; init; }
    public int ModifyingAttributeId { get; init; }
    public double SourceBaseValue { get; init; }
    public double SourceValue { get; init; }
    public string Operation { get; init; } = "";
    public double PenaltyMultiplier { get; init; } = 1;
    public double AppliedValue { get; init; }
    public double Before { get; init; }
    public double After { get; init; }
}
''';p.write_text(s,encoding='utf-8')
p=root/'src/application-csharp/EdenOS.Application/Fitting/Dogma/Models/FittingCalculationModels.cs';s=p.read_text(encoding='utf-8');s=s.replace('    public Dictionary<string, decimal> AttributeSnapshot', '    public Dictionary<string, EdenOS.Contracts.Fitting.FitAttributeExecutionTrace> AttributeTraces { get; set; } = new();\n    public Dictionary<string, decimal> AttributeSnapshot');p.write_text(s,encoding='utf-8')
p=root/'src/application-csharp/EdenOS.Application/Fitting/Dogma/DogmaFitAdapter.cs';s=p.read_text(encoding='utf-8');s=s.replace('            AttributeSnapshot = new Dictionary', '            AttributeTraces = summary.AttributeTraces,\n            AttributeSnapshot = new Dictionary');p.write_text(s,encoding='utf-8')
p=root/'src/application-csharp/EdenOS.Application/Fitting/Dogma/Engine/FittingCalculator.cs';s=p.read_text(encoding='utf-8');s=s.replace('var pass2 = RunPass2(pass1, fit, skills, dogmaContext);','var traces = new Dictionary<string, FitAttributeExecutionTrace>();\n        var pass2 = RunPass2(pass1, fit, skills, dogmaContext, traces);')
s=s.replace('return RunPass4(pass3, warnings, dogmaContext, includeAttributeSnapshot);','''var output = RunPass4(pass3, warnings, dogmaContext, includeAttributeSnapshot);
        foreach (var trace in traces.Values)
        {
            var final = pass3.ShipAttributes.GetValueOrDefault(dogmaContext.TryGetAttributeId(trace.Attribute));
            trace.IsComplete = Math.Abs(final - trace.FinalValue) < 1e-8;
            trace.FinalValue = final;
        }
        output.Summary.AttributeTraces = traces;
        return output;''')
s=s.replace('DogmaContext ctx)\n    {\n        ApplyShipEffects(fit, skills, ctx, pass1.ShipAttributes, pass1.Sources);','DogmaContext ctx, Dictionary<string, FitAttributeExecutionTrace> traces)\n    {\n        ApplyShipEffects(fit, skills, ctx, pass1.ShipAttributes, pass1.Sources, traces);')
a=s.index('    private static void ApplyShipEffects(');b=s.index('    private static double GetCharacterAttributeValue',a);part=s[a:b];part=part.replace('IReadOnlyList<SourceEntity>? sourcesOverride = null)', 'IReadOnlyList<SourceEntity>? sourcesOverride = null,\n        Dictionary<string, FitAttributeExecutionTrace>? traces = null)')
part=part.replace('SourceCategoryId: sourceCategoryId));','''SourceCategoryId: sourceCategoryId, TraceSource: new FitModifierExecutionStep
                        {
                            SourceTypeId = source.TypeId, SourceKind = source.Kind.ToString(), SourceIndex = source.RefIndex,
                            SourceState = source.State.ToString(), SkillLevel = source.SkillLevel, EffectId = typeEffect.EffectId,
                            ModifyingAttributeId = modifier.ModifyingAttributeId.Value,
                            SourceBaseValue = GetTypeAttributeValue(source.TypeId, modifier.ModifyingAttributeId.Value, ctx)
                        }));''')
part=part.replace('shipAttributes[attributeId] = ApplyEffects(current, effects, meta?.HighIsGood ?? false, meta?.Stackable ?? false);','''var trace = new FitAttributeExecutionTrace { Attribute = meta?.Name ?? attributeId.ToString(), BaseValue = current };
            shipAttributes[attributeId] = ApplyEffects(current, effects, meta?.HighIsGood ?? false, meta?.Stackable ?? false, trace.Steps);
            trace.FinalValue = shipAttributes[attributeId];
            if (traces is not null) traces[trace.Attribute] = trace;''')
# Include untouched attributes as real base reads, not guessed deltas.
part=part.replace('        foreach (var (attributeId, effects) in pending)', '''        if (traces is not null)
            foreach (var entry in shipAttributes)
            {
                var name = ctx.GetAttribute(entry.Key)?.Name ?? entry.Key.ToString();
                traces[name] = new FitAttributeExecutionTrace { Attribute=name, BaseValue=entry.Value, FinalValue=entry.Value };
            }
        foreach (var (attributeId, effects) in pending)''');s=s[:a]+part+s[b:]
s=s.replace('private sealed record PendingEffect(EffectOperator Operator, double SourceValue, int SourceCategoryId);','private sealed record PendingEffect(EffectOperator Operator, double SourceValue, int SourceCategoryId, FitModifierExecutionStep? TraceSource = null);')
a=s.index('    private static double ApplyEffects(');b=s.index('    private static bool HasPenalty',a);part=s[a:b];part=part.replace('bool stackable)', 'bool stackable, List<FitModifierExecutionStep>? trace = null)')
part=part.replace('        var current = baseValue;','''        var current = baseValue;
        void Record(PendingEffect e, double before, double applied, double penalty = 1)
        {
            if (trace is not null) trace.Add((e.TraceSource ?? new FitModifierExecutionStep()) with
            { Order=trace.Count+1, Operation=e.Operator.ToString(), SourceValue=e.SourceValue,
              Before=before, After=current, AppliedValue=applied, PenaltyMultiplier=penalty });
        }''')
part=part.replace('var selected = opEffects[0].SourceValue;', 'var selectedEffect = opEffects[0];\n                var selected = selectedEffect.SourceValue;').replace('                        selected = v;', '                        selectedEffect = opEffects[i];\n                        selected = v;')
part=part.replace('                current = selected;', '                var before = current;\n                current = selected;\n                Record(selectedEffect, before, selected);')
part=part.replace('                    current += ToOpValue(op, e.SourceValue);', '                    var before = current;\n                    current += ToOpValue(op, e.SourceValue);\n                    Record(e, before, ToOpValue(op, e.SourceValue));')
part=part.replace('new List<double>()','new List<(double Value, PendingEffect Effect)>()').replace('nonPenalty.Add(value)','nonPenalty.Add((value,e))').replace('penaltyNegative.Add(value)','penaltyNegative.Add((value,e))').replace('penaltyPositive.Add(value)','penaltyPositive.Add((value,e))')
part=part.replace('                current *= 1d + value;', '                var before = current;\n                current *= 1d + value.Value;\n                Record(value.Effect, before, 1d + value.Value);')
part=part.replace('Math.Abs(b).CompareTo(Math.Abs(a))', 'Math.Abs(b.Value).CompareTo(Math.Abs(a.Value))')
for name in ['penaltyPositive','penaltyNegative']:
 part=part.replace(f'                current *= 1d + {name}[i] * Math.Pow(PenaltyFactor, i * i);',f'''                var before = current;
                var penalty = Math.Pow(PenaltyFactor, i * i);
                var factor = 1d + {name}[i].Value * penalty;
                current *= factor;
                Record({name}[i].Effect, before, factor, penalty);''')
s=s[:a]+part+s[b:];p.write_text(s,encoding='utf-8')
