#define PROXYBRIDGE_API
#include <winsock2.h>
#include <windows.h>
#include <stdio.h>
#include "ld_metadata.h"
static int checks,events;static UINT64 last_flow;static DWORD last_pid;static UINT32 last_stage,last_status,last_error;static int last_result;
void check(BOOL ok,const char* label){if(!ok){printf("FAIL %s\n",label);ExitProcess(1);}checks++;}
void event(UINT64 f,DWORD p,UINT32 af,UINT16 port,UINT32 rule,UINT32 proxy,UINT32 stage,int result,UINT32 error,UINT32 status){events++;last_flow=f;last_pid=p;last_stage=stage;last_status=status;last_error=error;last_result=result;WSASetLastError(9999);}
int main(void)
{
    const char *target="C:\\Program Files\\WindowsApps\\OpenAI.Codex_26.930.7945.0_x64__2p2nqsd0c76g0\\app\\ChatGPT.exe";DWORD pid;
    ProxyBridge_ConfigureMetadata(event,target,3,1);ld_metadata_context(6,50000);ld_metadata_pid(95112);
    ld_metadata_decision("other.exe",443,FALSE,RULE_ACTION_PROXY,1);check(events==0,"unrelated executable excluded");
    ld_metadata_decision(target,80,FALSE,RULE_ACTION_PROXY,1);ld_metadata_decision(target,443,TRUE,RULE_ACTION_PROXY,1);check(events==0,"other ports/UDP excluded");
    ld_metadata_decision(target,443,FALSE,RULE_ACTION_PROXY,1);check(events==1&&last_pid==95112&&last_stage==LD_META_DECISION&&last_result==0,"target numeric decision emitted");UINT64 flow=last_flow;
    check(ld_metadata_lookup(6,50000,1,&pid)==flow&&pid==95112,"immutable flow lookup for relay");
    check(ld_metadata_lookup(4,50000,1,&pid)==0&&pid==0,"wrong family cannot correlate");check(ld_metadata_lookup(6,50000,2,&pid)==0,"wrong proxy cannot correlate");
    WSASetLastError(1234);ld_metadata_emit(flow,95112,6,50000,1,LD_META_UPSTREAM,1,10061,0);check(WSAGetLastError()==1234&&last_error==10061,"callback preserves Winsock error while recording failure");
    ld_metadata_http_reset();ld_metadata_http_status(403);ld_metadata_http_failure(10054);check(ld_metadata_http_get_status()==403&&ld_metadata_http_get_error()==10054,"status/error typed capture");
    ld_metadata_http_status(999999);check(ld_metadata_http_get_status()==0,"malformed status cannot become metadata");
    ld_metadata_http_reset();check(ld_metadata_http_get_status()==0&&ld_metadata_http_get_error()==0,"new handshake clears prior metadata only");
    ld_metadata_context(6,52048);ld_metadata_pid(95112);ld_metadata_decision(target,443,FALSE,RULE_ACTION_PROXY,1);check(ld_metadata_lookup(6,50000,1,&pid)==0,"bounded table collision drops stale correlation");
    check(ld_metadata_lookup(6,52048,1,&pid)==last_flow,"collision retains new flow");
    ld_metadata_context(4,50100);ld_metadata_pid(95112);ld_metadata_decision(target,443,FALSE,RULE_ACTION_DIRECT,0);check(last_result==1,"direct decision visible for target");
    int before=events;ld_metadata_context(4,50101);ld_metadata_pid(0);ld_metadata_decision(target,443,FALSE,RULE_ACTION_PROXY,1);check(events==before,"unattributed traffic excluded rather than logged broadly");
    printf("PASS %d native metadata assertions; actual pb_metadata.c only, no sockets, WinDivert or production DLL loaded.\n",checks);return 0;
}
