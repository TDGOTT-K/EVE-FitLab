"""Frozen referenced fits travel with a portable document, never into the user's library."""
def context_fits(fit,library):
    snapshots=fit.get('scenarioSnapshots',[])
    if not isinstance(snapshots,list) or len(snapshots)>150:raise ValueError('情景快照格式或数量无效')
    ids=[]
    for row in snapshots:
        if not isinstance(row,dict) or not isinstance(row.get('id'),str) or not row['id']:raise ValueError('情景快照缺少标识')
        ids.append(row['id'])
    if len(ids)!=len(set(ids)):raise ValueError('情景快照标识重复')
    return snapshots+[row for row in library if row.get('id') not in ids]
