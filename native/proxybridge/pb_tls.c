#include "pb_tls.h"
#include <windows.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

static unsigned int u16(const unsigned char *p) { return ((unsigned int)p[0] << 8) | p[1]; }

static int hostname_valid(const char *host, size_t length)
{
    size_t label = 0;
    if (!length || length > 253) return 0;
    for (size_t i = 0; i < length; i++) {
        unsigned char c = (unsigned char)host[i];
        if (c == '.') {
            if (!label || host[i-1] == '-') return 0;
            label = 0;
        } else {
            if (!((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') ||
                  (c >= '0' && c <= '9') || c == '-') || (!label && c == '-')) return 0;
            if (++label > 63) return 0;
        }
    }
    return label && host[length-1] != '-';
}

/* 1 = found, 0 = absent/invalid/unsupported, -1 = incomplete input. */
int ld_tls_server_name(const unsigned char *data, size_t size, char *host, size_t capacity)
{
    unsigned char hello[LD_TLS_PEEK_LIMIT];
    size_t offset = 0, used = 0, needed = 4, p, end, host_start = 0, host_length = 0;
    if (!host || !capacity) return 0;
    host[0] = 0;
    if (!data || size > LD_TLS_PEEK_LIMIT) return 0;
    while (used < needed) {
        if (size - offset < 5) return -1;
        if (data[offset] != 22 || data[offset+1] != 3) return 0;
        size_t record = u16(data + offset + 3);
        if (!record || record > 18432) return 0;
        if (size - offset - 5 < record) return -1;
        if (record > sizeof(hello) - used) return 0;
        memcpy(hello + used, data + offset + 5, record);
        used += record; offset += record + 5;
        if (used >= 4) {
            if (hello[0] != 1) return 0;
            needed = 4 + ((size_t)hello[1] << 16) + ((size_t)hello[2] << 8) + hello[3];
            if (needed > sizeof(hello) || needed < 4 + 34) return 0;
        }
    }
    /* legacy_version + random, session id, cipher suites, compression methods */
    p = 4 + 34;
    if (p >= needed || hello[p] > 32 || (size_t)hello[p] + 1 > needed - p) return 0;
    p += 1 + hello[p];
    if (needed - p < 2) return 0;
    size_t ciphers = u16(hello+p); p += 2;
    if (!ciphers || (ciphers & 1) || ciphers > needed-p) return 0;
    p += ciphers;
    if (p >= needed || !hello[p] || (size_t)hello[p] + 1 > needed-p) return 0;
    p += 1 + hello[p];
    if (p == needed) return 0;
    if (needed-p < 2 || u16(hello+p) != needed-p-2) return 0;
    p += 2; end = needed;
    int seen_sni = 0;
    while (p < end) {
        if (end-p < 4) return 0;
        unsigned int type = u16(hello+p); size_t length = u16(hello+p+2); p += 4;
        if (length > end-p) return 0;
        if (type == 0) {
            if (seen_sni++ || length < 2 || u16(hello+p) != length-2) return 0;
            size_t q = p+2, list_end = p+length;
            while (q < list_end) {
                if (list_end-q < 3) return 0;
                unsigned int name_type = hello[q]; size_t name_length = u16(hello+q+1); q += 3;
                if (name_length > list_end-q) return 0;
                if (name_type == 0) {
                    if (host_length || !hostname_valid((const char *)hello+q, name_length)) return 0;
                    host_start = q; host_length = name_length;
                }
                q += name_length;
            }
        }
        p += length;
    }
    if (!host_length || host_length >= capacity) return 0;
    for (size_t i = 0; i < host_length; i++) {
        unsigned char c = hello[host_start+i]; host[i] = (char)(c >= 'A' && c <= 'Z' ? c+32 : c);
    }
    host[host_length] = 0;
    return 1;
}

int ld_tls_is_openai_host(const char *host)
{
    static const char *suffixes[] = {"chatgpt.com", "openai.com", "oaistatic.com", "oaiusercontent.com"};
    if (!host || !hostname_valid(host, strlen(host))) return 0;
    size_t n = strlen(host);
    for (size_t i = 0; i < sizeof(suffixes)/sizeof(suffixes[0]); i++) {
        size_t s = strlen(suffixes[i]);
        if (n >= s && _stricmp(host+n-s, suffixes[i]) == 0 && (n == s || host[n-s-1] == '.')) return 1;
    }
    return 0;
}

int ld_tls_peek_server_name(SOCKET client, char *host, size_t capacity, DWORD timeout_ms)
{
    unsigned char *buffer = (unsigned char *)malloc(LD_TLS_PEEK_LIMIT);
    ULONGLONG deadline = GetTickCount64() + timeout_ms;
    int result = 0, previous = 0;
    if (host && capacity) host[0] = 0;
    if (!buffer || !host || !capacity) { free(buffer); return 0; }
    while (GetTickCount64() < deadline) {
        ULONGLONG remaining = deadline - GetTickCount64();
        if (remaining > timeout_ms) break;
        fd_set reads; FD_ZERO(&reads); FD_SET(client, &reads);
        struct timeval wait = {(long)(remaining / 1000), (long)(remaining % 1000) * 1000};
        if (select(0, &reads, NULL, NULL, &wait) <= 0) break;
        int got = recv(client, (char *)buffer, LD_TLS_PEEK_LIMIT, MSG_PEEK);
        if (got <= 0) break;
        if (got != previous) {
            result = ld_tls_server_name(buffer, (size_t)got, host, capacity);
            if (result >= 0 || got == LD_TLS_PEEK_LIMIT) break;
            previous = got;
        }
        Sleep(5); /* An incomplete peek stays readable; bound both CPU and wait. */
    }
    free(buffer);
    if (result != 1) host[0] = 0;
    return result == 1;
}

/* Enhanced mode uses an unauthenticated loopback HTTP proxy. Read only headers,
   preserving any tunneled bytes that follow them in the same TCP segment. */
int ld_tls_http_connect(SOCKET proxy, const char *host, unsigned short port, int *status, DWORD *error)
{
    char request[600], response[8193]; int written = 0, used = 0;
    if (status) *status = 0;
    if (error) *error = 0;
    if (!host || !ld_tls_is_openai_host(host) || !port) return -1;
    int length = snprintf(request, sizeof(request), "CONNECT %s:%u HTTP/1.1\r\nHost: %s:%u\r\nProxy-Connection: keep-alive\r\n\r\n", host, port, host, port);
    if (length <= 0 || length >= (int)sizeof(request)) return -1;
    while (written < length) {
        int n = send(proxy, request+written, length-written, 0);
        if (n <= 0) { if (error && n == SOCKET_ERROR) *error = WSAGetLastError(); return -1; }
        written += n;
    }
    while (used < (int)sizeof(response)-1) {
        int n = recv(proxy, response+used, 1, 0);
        if (n != 1) { if (error && n == SOCKET_ERROR) *error = WSAGetLastError(); return -1; }
        used++;
        if (used >= 4 && memcmp(response+used-4, "\r\n\r\n", 4) == 0) {
            response[used] = 0;
            if (used < 12 || strncmp(response, "HTTP/1.", 7) ||
                (response[7] != '0' && response[7] != '1') || response[8] != ' ' ||
                response[9] < '1' || response[9] > '9' || response[10] < '0' || response[10] > '9' ||
                response[11] < '0' || response[11] > '9' || (response[12] != ' ' && response[12] != '\r')) return -1;
            int code = (response[9]-'0')*100 + (response[10]-'0')*10 + response[11]-'0';
            if (status) *status = code;
            return code == 200 ? 0 : -1;
        }
    }
    return -1;
}
