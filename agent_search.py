"""Task-oriented catalog queries. All values/filtering/sorting come from native MCP."""


def page(agent, query):
    query = {'limit': 20, **query}
    if type(query['limit']) is not int or not 1 <= query['limit'] <= 30:
        raise ValueError('query.limit must be 1..30')
    result = agent.native('catalog_search', {'query': query})
    result['items'] = [{k: ({lang: text for lang, text in row[k].items() if lang in ('en', 'zh')} if k == 'names' else row[k]) for k in ('typeId', 'names', 'groupId', 'categoryId', 'metaGroupId')} for row in result['items']]
    result['groupFacetCount'] = len(result.get('groups', []))
    groups = result.get('groups', [])
    result['groupsNextCall'] = ({'tool': 'fitlab_result', 'arguments': {'resultId': agent.store(groups), 'offset': 20, 'limit': 20}} if len(groups) > 20 else None)
    result['groups'] = groups[:20]
    result['groupsTruncated'] = result['groupFacetCount'] > 20
    result['nextCall'] = ({'tool': 'fitlab_search', 'arguments': {'query': {**query, 'cursor': result['nextCursor']}}}
                          if result.get('nextCursor') else None)
    return result


def search(agent, args):
    if set(args) - {'names', 'categoryId', 'query'}:
        raise ValueError('Use names/categoryId OR query')
    if 'query' in args:
        if set(args) != {'query'} or not isinstance(args['query'], dict):
            raise ValueError('query cannot be combined with batch names/categoryId')
        result = page(agent, args['query'])
        return {**agent.bounded(result), 'nextCall': result['nextCall'], 'total': result['total']}
    names = args.get('names')
    if not isinstance(names, list) or not 1 <= len(names) <= 30 or any(not isinstance(n, str) or not n.strip() for n in names):
        raise ValueError('Provide 1..30 nonempty names, or query:{} to browse')
    rows = []
    for name in names:
        query = {'text': name, 'limit': 5}
        if 'categoryId' in args:
            query['categoryId'] = args['categoryId']
        result = page(agent, query)
        rows.append({'query': name, 'source': result['source'], 'total': result['total'],
                     'nextCursor': result['nextCursor'], 'nextCall': result['nextCall'],
                     'candidates': [{k: row[k] for k in ('typeId', 'names', 'groupId', 'categoryId', 'metaGroupId')}
                                    for row in result['items']]})
    return agent.bounded(rows)
