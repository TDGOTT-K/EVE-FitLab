"""Save UI metadata only after a source-bound native transaction has saved the fit.

Deterministic operation IDs let an identical request recover a lost response or
an interrupted UI-library write. Native session files are never edited here.
"""
import copy,hashlib,json,uuid
from nengine_adapter import bridge,native_fit,validate_source_binding
from nengine_bridge import NEngineError
from nengine_edit_commands import commands_between

class SaveConflict(ValueError):pass

def native_save(fit,previous,transaction,client=None,editing=None):
    client=client or bridge();status=client.discover();build=status['source']['source']['buildNumber']
    validate_source_binding(fit,client)
    candidate=native_fit(fit,build)
    expected=client.call('fit_analyze',{'fit':candidate})['result']['fitHash']
    if editing is not None:
        if not isinstance(editing,dict) or set(editing)!={'id','revision'} or type(editing['revision']) is not int or editing['revision']<0:raise ValueError('编辑会话引用无效')
        inspected=client.call('fit_inspect',{'sessionId':editing['id']})['result']
        if inspected['analysis']['fitHash']!=expected:raise SaveConflict('编辑会话与提交装配不同，未保存')
        result=client.call('fit_execute',{'sessionId':editing['id'],'revision':editing['revision'],'requestId':transaction+'-save','operation':'save'})['result']
        document=client.call('fit_export',{'sessionId':editing['id'],'snapshot':'saved'})['result']
        if document['fitHash']!=expected:raise SaveConflict('原生已保存快照已变化，未覆盖装配库')
        return {'id':editing['id'],'revision':result['appliedRevision'],'fitHash':expected,'source':document['source'],
                'engineVersion':status['engineVersion'],'contractRevision':status['publicContract']['revision']}
    reference=(previous or {}).get('nativeSession')
    ident=reference['id'] if reference else 'fit-'+str(uuid.UUID(fit['id']))
    if reference:
        revision=reference['revision'];commands=commands_between(native_fit(previous,build),candidate)
    else:
        revision=0;commands=[]
        try:client.call('fit_create',{'sessionId':ident,'fit':candidate,'allowIncompleteDraft':True})
        except NEngineError as error:
            if error.error.get('code')!='OUTPUT_EXISTS':raise
            existing=client.call('fit_inspect',{'sessionId':ident})['result']['session']
            original=client.call('fit_analyze',{'fit':existing['initial']})['result']['fitHash']
            if original!=expected or not existing['allowIncompleteDraft']:raise SaveConflict('原生会话身份已被其他装配占用')
    if commands:
        result=client.call('fit_execute',{'sessionId':ident,'revision':revision,'requestId':transaction+'-apply',
            'operation':'apply','commands':commands})['result']
        revision=result['appliedRevision']
    result=client.call('fit_execute',{'sessionId':ident,'revision':revision,'requestId':transaction+'-save','operation':'save'})['result']
    document=client.call('fit_export',{'sessionId':ident,'snapshot':'saved'})['result']
    if document['fitHash']!=expected:raise SaveConflict('原生已保存快照已变化，未覆盖装配库')
    return {'id':ident,'revision':result['appliedRevision'],'fitHash':expected,'source':document['source'],
            'engineVersion':status['engineVersion'],'contractRevision':status['publicContract']['revision']}

def save_to_library(validated,library,write,now,client=None):
    fit=copy.deepcopy(validated)
    token=fit.pop('_saveRequestId',None)
    if not isinstance(token,str) or not 1<=len(token)<=100 or not token.isascii():raise ValueError('保存请求缺少有效事务标识')
    for field in ('nativeSession','saveReceipt','_workingDraft'):fit.pop(field,None)
    previous=next((row for row in library['fits'] if row['id']==fit.get('id')),None)
    if not fit.get('id'):fit['id']=str(uuid.uuid5(uuid.NAMESPACE_URL,'fitlab-save:'+token))
    # New user-defined identities are UUIDs; engine session names never accept paths.
    if not previous:
        try:uuid.UUID(fit['id'])
        except (ValueError,TypeError,AttributeError):raise ValueError('新装配标识必须是UUID')
    digest=hashlib.sha256(json.dumps(fit,sort_keys=True,separators=(',',':'),ensure_ascii=False,allow_nan=False).encode()).hexdigest()
    previous=next((row for row in library['fits'] if row['id']==fit['id']),None)
    if previous and previous.get('saveReceipt',{}).get('requestId')==token:
        if previous['saveReceipt']['inputHash']!=digest:raise SaveConflict('同一保存请求不能对应不同装配内容')
        return copy.deepcopy(previous)
    if previous and fit.get('revision')!=previous['revision']:raise SaveConflict('此装配已在另一窗口修改，请重新打开后再编辑。')
    transaction='ui-'+hashlib.sha256((fit['id']+'\n'+token+'\n'+digest).encode()).hexdigest()
    editing=fit.pop('_editSession',None)
    fit['nativeSession']=native_save(fit,previous,transaction,client,editing)
    fit['revision']=(previous['revision'] if previous else 0)+1;fit['updatedAt']=now
    fit['saveReceipt']={'requestId':token,'inputHash':digest}
    updated={**library,'fits':[row for row in library['fits'] if row['id']!=fit['id']]+[fit]}
    write(updated)
    return copy.deepcopy(fit)
