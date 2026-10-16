#include <winsock2.h>
#include <windows.h>
#include <string.h>
#include "ld_metadata.h"
/* Numeric-only, target-scoped diagnostics. Never copy packets, hostnames or headers.
   A bounded table is independent of routing/connection tracking; collisions drop metadata. */
#define LD_META_SLOTS 4096
typedef struct { UINT64 flow; DWORD pid; UINT32 family, proxy; UINT16 port; } LD_META_ROW;
static LD_META_ROW ld_rows[LD_META_SLOTS];
static SRWLOCK ld_lock=SRWLOCK_INIT;
static volatile LONG64 ld_sequence;
static MetadataCallback ld_callback;
static char ld_target[1024];
static UINT32 ld_rule, ld_proxy;
static __declspec(thread) UINT32 ld_family, ld_http_status, ld_http_error;
static __declspec(thread) UINT16 ld_port;
static __declspec(thread) DWORD ld_pid;
PROXYBRIDGE_API void ProxyBridge_ConfigureMetadata(MetadataCallback callback, const char *target, UINT32 rule, UINT32 proxy)
{
    ld_callback=NULL;
    if(!target || !rule || !proxy || strlen(target)>=sizeof(ld_target)) return;
    strcpy_s(ld_target,sizeof(ld_target),target); ld_rule=rule; ld_proxy=proxy;
    ld_callback=callback; /* Configured once before Start; callback rooted until host exit. */
}
void ld_metadata_context(UINT32 family, UINT16 port) { ld_family=family;ld_port=port;ld_pid=0; }
void ld_metadata_pid(DWORD pid) { ld_pid=pid; }
void ld_metadata_emit(UINT64 flow,DWORD pid,UINT32 family,UINT16 port,UINT32 proxy,UINT32 stage,int result,UINT32 error,UINT32 status)
{
    MetadataCallback callback=ld_callback;
    if(!callback || !flow || !pid || (family!=4 && family!=6)) return;
    DWORD last=GetLastError();int wsa=WSAGetLastError();
    callback(flow,pid,family,port,ld_rule,proxy,stage,result,error,status);
    WSASetLastError(wsa);SetLastError(last); /* Logging cannot change caller error handling. */
}
void ld_metadata_decision(const char *path,UINT16 dest_port,BOOL udp,RuleAction action,UINT32 proxy)
{
    if(!ld_callback || udp || dest_port!=443 || !ld_pid || !ld_port || !path || _stricmp(path,ld_target)) return;
    UINT64 flow=(UINT64)InterlockedIncrement64(&ld_sequence);
    UINT32 slot=(((UINT32)ld_port)*2+(ld_family==6))%LD_META_SLOTS;
    AcquireSRWLockExclusive(&ld_lock);
    ld_rows[slot].flow=flow;ld_rows[slot].pid=ld_pid;ld_rows[slot].family=ld_family;
    ld_rows[slot].port=ld_port;ld_rows[slot].proxy=proxy;
    ReleaseSRWLockExclusive(&ld_lock);
    ld_metadata_emit(flow,ld_pid,ld_family,ld_port,proxy,LD_META_DECISION,(int)action,0,0);
}
UINT64 ld_metadata_lookup(UINT32 family,UINT16 port,UINT32 proxy,DWORD *pid)
{
    UINT64 flow=0;*pid=0;if(!ld_callback || proxy!=ld_proxy) return 0;
    UINT32 slot=((UINT32)port*2+(family==6))%LD_META_SLOTS;
    AcquireSRWLockShared(&ld_lock);LD_META_ROW row=ld_rows[slot];
    if(row.family==family && row.port==port && row.proxy==proxy){flow=row.flow;*pid=row.pid;}
    ReleaseSRWLockShared(&ld_lock);return flow;
}
void ld_metadata_http_reset(void) { ld_http_status=0;ld_http_error=0; }
void ld_metadata_http_status(int status) { ld_http_status=status>=100 && status<=999?(UINT32)status:0; }
UINT32 ld_metadata_http_get_status(void) { return ld_http_status; }

void ld_metadata_http_failure(UINT32 error) { ld_http_error=error; }
UINT32 ld_metadata_http_get_error(void) { return ld_http_error; }
