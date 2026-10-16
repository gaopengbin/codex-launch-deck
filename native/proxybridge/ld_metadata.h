#ifndef LD_METADATA_H
#define LD_METADATA_H
#include <windows.h>
#include "ProxyBridge.h"
enum { LD_META_DECISION=1, LD_META_ACCEPT=2, LD_META_UPSTREAM=3, LD_META_CONNECT=4, LD_META_CLOSE=5 };
void ld_metadata_context(UINT32 family, UINT16 port);
void ld_metadata_pid(DWORD pid);
void ld_metadata_decision(const char *path, UINT16 dest_port, BOOL udp, RuleAction action, UINT32 proxy);
UINT64 ld_metadata_lookup(UINT32 family, UINT16 port, UINT32 proxy, DWORD *pid);
void ld_metadata_emit(UINT64 flow, DWORD pid, UINT32 family, UINT16 port, UINT32 proxy, UINT32 stage, int result, UINT32 error, UINT32 status);
void ld_metadata_http_reset(void);
void ld_metadata_http_status(int status);
UINT32 ld_metadata_http_get_status(void);
void ld_metadata_http_failure(UINT32 error);
UINT32 ld_metadata_http_get_error(void);
#endif
