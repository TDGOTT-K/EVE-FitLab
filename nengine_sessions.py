"""Explicit transport for native editing. No shadow state or automatic retries.

Inputs/outputs are public engine objects, so CLI can operate the same session.
UI layout, scenarios and analysis selection do not become fitting commands.
"""
from nengine_adapter import bridge

OPERATIONS={
    'create':('fit_create',{'sessionId','fit','allowIncompleteDraft'}),
    'import':('fit_import',{'sessionId','document'}),
    'inspect':('fit_inspect',{'sessionId','context'}),
    'preview':('fit_preview',{'sessionId','revision','commands','context'}),
    'execute':('fit_execute',{'sessionId','revision','requestId','operation','commands','context'}),
    'export':('fit_export',{'sessionId','snapshot'}),
}

def request(action,arguments,client=None):
    if action=='prepare':
        if not isinstance(arguments,dict) or set(arguments)!={'before','after'}:raise ValueError('编辑准备需要before和after装配快照')
        from nengine_edit_commands import prepare_ui_edit
        client=client or bridge();status=client.discover()
        return {'ok':True,'result':prepare_ui_edit(arguments['before'],arguments['after'],status['source']['source']['buildNumber'])}
    if action not in OPERATIONS:raise ValueError('未知装配事务操作')
    name,allowed=OPERATIONS[action]
    if not isinstance(arguments,dict) or set(arguments)-allowed:raise ValueError('装配事务包含未知字段')
    client=client or bridge()
    client.discover()
    # Preserve expected revision, request identity and raw engine diagnostics.
    # Never generate another request ID after a lost execute response.
    return client.call(name,arguments)
