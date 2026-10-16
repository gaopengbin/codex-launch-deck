#ifndef LD_TLS_H
#define LD_TLS_H
#include <winsock2.h>
#include <stddef.h>

/* Only public ClientHello metadata is read; application TLS bytes are never consumed. */
#define LD_TLS_PEEK_LIMIT 65536
int ld_tls_server_name(const unsigned char *data, size_t size, char *host, size_t capacity);
int ld_tls_peek_server_name(SOCKET client, char *host, size_t capacity, DWORD timeout_ms);
int ld_tls_is_openai_host(const char *host);
int ld_tls_http_connect(SOCKET proxy, const char *host, unsigned short port, int *status, DWORD *error);
#endif
