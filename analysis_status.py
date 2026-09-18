"""Presentation classification of public diagnostics; never evaluates game rules."""

def classify_report(report):
    native=report['native']
    admission=[*native.get('errors',[]),*(w for w in native.get('warnings',[]) if w['code']=='RESOURCE_EXCEEDED')]
    coverage=native.get('staticCoverageComplete') is True and native.get('validationState') in (None,'complete_static_checks')
    missing=[{'scope':'static','reason':c.get('reason'),'source':c} for c in native.get('coverage',[]) if c.get('status')=='unsupported_static']
    if not coverage and not missing:missing.append({'scope':'static','reason':'STATIC_COVERAGE_INCOMPLETE'})
    if native.get('capacitorUnavailableReason'):
        missing.append({'scope':'capacitor_load','reason':native['capacitorUnavailableReason'],
                        'exclusions':[c for c in native.get('capacitorContributions',[]) if c.get('state')!='available']})
    selection=report.get('outputSelection',{})
    if selection.get('completeSelection') is not True and selection.get('status')!='empty_selection':
        missing.append({'scope':'selected_output','reason':selection.get('status') or 'OUTPUT_INCOMPLETE','exclusions':selection.get('exclusions',[])})
    cap=report.get('capacitorScenario')
    if cap is not None:
        if cap.get('state')!='available':missing.append({'scope':'capacitor','reason':cap.get('reason'),'diagnostic':cap.get('diagnostic')})
        elif cap.get('result',{}).get('average',{}).get('complete') is not True:
            missing.append({'scope':'capacitor_average','reason':'AVERAGE_INCOMPLETE','exclusions':cap.get('result',{}).get('average',{}).get('exclusions',[])})
    legality='invalid' if admission else 'valid' if coverage else 'unknown'
    completeness='complete' if not missing else 'partial' if native.get('attributes') or native.get('resources') else 'unsupported'
    state='invalid' if legality=='invalid' else completeness if completeness!='complete' else 'valid'
    return {'state':state,'legality':legality,'completeness':completeness,'admissionIssues':admission,'unavailable':missing}
