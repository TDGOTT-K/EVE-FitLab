"""Bind UI sources to existing native periodic energy queries; no energy formula."""
import math,json
from nengine_adapter import bridge,analyze


def attach_capacitor(fit,report,fits):
    horizon=fit.get('capacitorHorizon',300)
    if type(horizon) not in (int,float) or not math.isfinite(horizon) or not 1<=horizon<=3600:
        report['capacitorScenario']={'state':'unavailable','reason':'观察窗口必须为1–3600秒'};return
    query={'horizonSeconds':horizon,'initialFraction':1,'failurePolicy':'stop_failed_consumer',
           'supplyPolicy':'fit_inventory','external':[],
           'sampleTimesSeconds':[horizon*i/60 for i in range(61)]}
    sources=[];scenario=fit.get('scenario') or {}
    try:
        initial=scenario.get('ownCapacitorFraction',1)
        if type(initial) not in (int,float) or not math.isfinite(initial) or not 0<=initial<=1:raise ValueError('本舰初始电量比例必须为0–1')
        query['initialFraction']=initial
        def source_fit(identity):
            if not identity:raise ValueError('请在情景画布选择吸电目标装配')
            source=next((f for f in fits if f.get('id')==identity),None)
            if source is None:raise ValueError('能量来源或吸电目标已删除，请重新选择')
            result=analyze({**source,'scenario':{},'activeScenarioId':None})
            if not result['isValid']:raise ValueError(source['name']+' 尚未通过原生装配校验')
            return source,result
        def amount(field):
            value=scenario.get(field)
            if value is None:raise ValueError('请在情景画布右键声明'+('目标' if field=='targetCapacitorGj' else '敌方来源')+'固定电量')
            if type(value) not in (int,float) or not math.isfinite(value) or value<0:raise ValueError('固定电量必须为非负有限GJ数值')
            return value
        local_nos=[e for e in report['native'].get('energy',{}).values() if e['active'] and e['operation']=='nosferatu']
        if local_nos:
            target,target_report=source_fit(scenario.get('targetFitId'))
            native=target_report['native'];capacity=(native.get('capacitor') or {}).get('recharge',{}).get('capacity')
            resistance=native.get('energyWarfareMultiplier')
            if capacity is None or resistance is None:raise ValueError('吸电目标容量或能量抗性不可计算')
            fixed=amount('targetCapacitorGj');distance=scenario.get('distance',10000)
            query['nosTargets']={e['instanceId']:{'capacityGj':capacity,'amountGj':fixed,'warfareMultiplier':resistance,'distanceMeters':distance} for e in local_nos}
            sources.append({'id':target['id'],'name':target['name'],'revision':target.get('revision'),'fitHash':native['fitHash'],
                'nativeFit':target_report['nativeFit'],'source':target_report['source'],'operation':'nos_target','distanceMeters':distance,'amountGj':fixed})
        for field,operation in [('supportFitId','transmit'),('hostileFitId','neutralize')]:
            identity=scenario.get(field)
            if not identity:continue
            source,source_report=source_fit(identity)
            distance=scenario.get('supportDistance' if field=='supportFitId' else 'hostileDistance',10000)
            if type(distance) not in (int,float) or not math.isfinite(distance) or not 0<=distance<=1e7:
                raise ValueError('能量来源距离无效')
            matching=[e for e in source_report['native'].get('energy',{}).values() if e['active'] and e['operation'] in (('neutralize','nosferatu') if operation=='neutralize' else ('transmit',))]
            if not matching:raise ValueError(source['name']+' 没有可用的已启用'+('传电' if operation=='transmit' else '毁电/吸电')+'模块')
            for energy in matching:
                entry={'id':field+'-'+energy['instanceId'],'sourceFit':source_report['nativeFit'],'moduleId':energy['instanceId'],'distanceMeters':distance}
                if energy['operation']=='nosferatu':entry['sourceAmountGj']=amount('hostileCapacitorGj')
                query['external'].append(entry)
            sources.append({'id':identity,'name':source['name'],'revision':source.get('revision'),
                'fitHash':source_report['native']['fitHash'],'operation':operation,'distanceMeters':distance,'operations':sorted({e['operation'] for e in matching}),
                'amountGj':scenario.get('hostileCapacitorGj') if any(e['operation']=='nosferatu' for e in matching) else None})
        result=bridge().call('capacitor_scenario',{'fit':report['nativeFit'],'query':query})['result']
        report['capacitorScenario']={'state':'available','result':result,'sources':sources,
            'request':{'fit':report['nativeFit'],'query':query}}
    except ValueError as error:
        reason=str(error);diagnostic=None
        try:
            diagnostic=json.loads(reason).get('error')
            if diagnostic:
                names={'CAP_SCENARIO_FIT':'本舰或外部来源装配尚未通过原生校验，请查看装配诊断','CAP_SCENARIO_SUPPLY':'注电需要明确弹种、实际装弹量和有限货舱供应','CAP_SCENARIO_NOS_TARGET':'本舰吸电缺少对方电量、容量、抗性或距离条件','CAP_SCENARIO_NOS_SOURCE':'外来吸电缺少来源电容条件'}
                reason=names.get(diagnostic.get('code'),diagnostic.get('message',reason))
        except (ValueError,AttributeError):pass
        report['capacitorScenario']={'state':'unavailable','reason':reason,'diagnostic':diagnostic,'sources':sources,
            'request':{'fit':report['nativeFit'],'query':query}}
