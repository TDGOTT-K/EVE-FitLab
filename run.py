"""Start FitLab with the pinned NEngine MCP backend."""
import os

def main():
    import server
    from nengine_adapter import bridge
    client = bridge()
    http = None
    try:
        status = client.discover()
        port = int(os.environ.get('FITLAB_API_PORT', '5208'))
        http = server.ThreadingHTTPServer(('127.0.0.1', port), server.Handler)
        print(f"NEngine {status['engineVersion']} — http://127.0.0.1:{port}/", flush=True)
        try:
            server.ensure_sso_callback(http)
        except ValueError as error:
            print(str(error), flush=True)
        http.serve_forever()
    except KeyboardInterrupt:
        pass
    finally:
        if http is not None:
            http.server_close()
        client.close()

if __name__ == '__main__':
    main()
