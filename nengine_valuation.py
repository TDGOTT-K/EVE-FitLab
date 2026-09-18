"""Translate UI inputs and deliver the public valuation result unchanged."""
from nengine_adapter import bridge,native_fit,validate_source_binding
from market_prices import snapshot

def value_fit(fit,market=None):
    client=bridge();status=client.discover()
    validate_source_binding(fit,client)
    native=native_fit(fit,status['source']['source']['buildNumber'])
    market=market if market is not None else fit.get('valuationSnapshot')
    market=market if market is not None else snapshot()
    # Transport projection only: select quoted type IDs; no quantities or totals.
    ids=set()
    def visit(value):
        if isinstance(value,dict):
            for key,item in value.items():
                if key in ('typeId','shipTypeId','chargeTypeId') and type(item) is int:ids.add(str(item))
                else:visit(item)
        elif isinstance(value,list):
            for item in value:visit(item)
    visit(native)
    selected={'source':market['source'],'prices':{key:value for key,value in market['prices'].items() if key in ids}}
    request={'fit':native,'snapshot':selected}
    return {'valuation':client.call('fit_valuation',request)['result'],'request':request}
