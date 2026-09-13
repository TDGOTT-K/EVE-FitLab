from pathlib import Path
p=Path('engine-view.js');s=p.read_text(encoding='utf-8').replace("['追踪来源','引擎执行记录']","['明细范围','当前返回值']")
s=s.replace("const sourceTerms=isSkill?", "const sourceUnit=op==='PostPercent'?'%':'';\n  const sourceTerms=isSkill?")
s=s.replace("['每级修正',step.sourceBaseValue]", "['每级修正',`${step.sourceBaseValue}${sourceUnit}`]")
s=s.replace("result:step.sourceValue,terms:sourceTerms", "result:`${step.sourceValue}${sourceUnit}`,terms:sourceTerms")
s=s.replace("['来源修正值',step.sourceValue,null,sourceDetail]", "['来源修正值',`${step.sourceValue}${sourceUnit}`,null,sourceDetail]")
p.write_text(s,encoding='utf-8')
