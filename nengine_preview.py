"""Read-only command preview through the public engine, with normal UI projections."""
import copy
from nengine_adapter import bridge,validate_source_binding
from nengine_edit_commands import prepare_ui_edit

def preview_fit(before,after,analyze):
    client=bridge();status=client.discover()
    validate_source_binding(before,client);validate_source_binding(after,client)
    prepared=prepare_ui_edit(before,after,status['source']['source']['buildNumber'])
    previews=[]
    def query(context):
        arguments={'fit':prepared['fit'],'commands':prepared['commands'],'context':copy.deepcopy(context)}
        result=client.call('fit_preview_input',arguments)['result']
        previews.append({'request':arguments,'result':result})
        return result['analysis']
    report=analyze(after,native_query=query)
    report['editPreview']=previews[-1]['result']
    report['editPreviewRequests']=[p['request'] for p in previews]
    return report
