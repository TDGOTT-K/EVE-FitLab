from pathlib import Path
p=Path(r'D:/IT/EVE/EdenOsRewrite/tests/EdenOS.Application.Tests/DogmaFittingIntegrationTests.cs');s=p.read_text(encoding='utf-8');needle='    [Fact]\n    public void DogmaFitPassesValidation()';test='''    [Fact]
    public void AttributeExecutionTracesReplayActualOperations()
    {
        var result = TestWorkspaceSupport.Require(fitService.Validate(new ValidateFitRequest
        { Snapshot = CreateDogmaBrawlerFit("execution-trace") }));
        Assert.NotEmpty(result.Attributes.AttributeTraces);
        Assert.Contains(result.Attributes.AttributeTraces.Values, t => t.Steps.Count > 0);
        foreach (var trace in result.Attributes.AttributeTraces.Values.Where(t => t.IsComplete))
        {
            var current = trace.BaseValue;
            foreach (var step in trace.Steps)
            {
                Assert.Equal(current, step.Before, 8);
                Assert.True(step.SourceTypeId > 0);
                Assert.InRange(step.PenaltyMultiplier, 0, 1);
                current = step.Operation switch
                {
                    "PreAssign" or "PostAssign" => step.AppliedValue,
                    "ModAdd" or "ModSub" => current + step.AppliedValue,
                    _ => current * step.AppliedValue
                };
                Assert.Equal(current, step.After, 8);
            }
            Assert.Equal(current, trace.FinalValue, 8);
        }
    }

''';assert needle in s;s=s.replace(needle,test+needle);p.write_text(s,encoding='utf-8')
