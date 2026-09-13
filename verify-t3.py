import server
sub=[t for t in server.CATALOG if t['kind']=='subsystem']
for hull in [29984,29986,29988,29990]:
 items=[next(t for t in sub if t['attrs']['1380']==hull and t['attrs']['1366']==125+i) for i in range(4)]
 f={'shipId':hull,'name':'T3 acceptance','skills':[],'slots':[{'key':'subsystem-'+str(i),'kind':'subsystem','item':t['id'],'state':'Online'} for i,t in enumerate(items)]}
 result=server.analyze(f)
 assert result['isValid'],result['issues']
 assert result['attributes']['slotUsage'][0]['available']>0
 assert next(s for s in result['attributes']['slotUsage'] if s['kind']=='Subsystem')['available']==4
 f['slots'][0]['item']=next(t['id'] for t in sub if t['attrs']['1380']!=hull)
 try:server.validate_fit(f);raise AssertionError('cross hull accepted')
 except ValueError:pass
 print(hull,'four subsystems validated')
assert not server.analyze({'shipId':29984,'name':'missing','slots':[],'skills':[]})['isValid']
print('T3 incomplete and incompatible cases passed')
