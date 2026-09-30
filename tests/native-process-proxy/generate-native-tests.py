from pathlib import Path
import hashlib,sys,tempfile,shutil
root=Path(__file__).resolve().parents[2];src=root/'native'/'proxybridge';out=Path(sys.argv[1]) if len(sys.argv)>1 else Path(tempfile.mkdtemp(prefix='launchdeck-native-tests-'));out.mkdir(parents=True,exist_ok=True)
shutil.copy2(src/'ld_safety.h',out/'ld_safety.h')
core=(src/'ProxyBridge.c').read_text(encoding='utf-8-sig');a=core.index('DWORD WINAPI cleanup_worker');b=core.index('PROXYBRIDGE_API BOOL ProxyBridge_Start',a);worker=core[a:b]
a=core.index('BOOL WINAPI DllMain');detach=core[a:]
fixture='''#include <winsock2.h>
#include <windows.h>
#include "ld_safety.h"
typedef struct R { char *target_hosts,*target_ports,*target_domains;struct R *next; } PROCESS_RULE;
typedef struct { SOCKET udp_tcp_ctrl,udp_send_sock; } PROXY_CONFIG;
DWORD g_current_process_id;BOOL running=FALSE;int g_proxy_config_count=0;
PROXY_CONFIG g_proxy_configs[1];PROCESS_RULE *rules_list=NULL;SRWLOCK g_rules_lock=SRWLOCK_INIT;
BOOL ProxyBridge_Stop(void) { return TRUE; }
HANDLE hold_ready,hold_forever;
DWORD WINAPI hold_lock(LPVOID arg) { AcquireSRWLockExclusive(&g_rules_lock);SetEvent(hold_ready);WaitForSingleObject(hold_forever,INFINITE);return 0; }
__declspec(dllexport) BOOL FixtureBeginPinned(void)
{
 if(!ld_pin_module((const void*)&FixtureBeginPinned)) return FALSE;
 running=TRUE;hold_ready=CreateEvent(NULL,TRUE,FALSE,NULL);hold_forever=CreateEvent(NULL,TRUE,FALSE,NULL);
 HANDLE thread=CreateThread(NULL,0,hold_lock,NULL,0,NULL);
 if(thread==NULL) return FALSE;CloseHandle(thread);
 return WaitForSingleObject(hold_ready,1000)==WAIT_OBJECT_0;
}
'''+detach
(out/'LifecycleFixture.c').write_text(fixture,encoding='utf-8')
main='''#include <winsock2.h>
#include <windows.h>
#include <stdio.h>
#include "ld_safety.h"
volatile BOOL running;HANDLE g_cleanup_stop,wait_entered;volatile LONG maintenance_calls;
void cleanup_stale_connections(void) { InterlockedIncrement(&maintenance_calls); }
void cleanup_stale_pid_cache(void) { InterlockedIncrement(&maintenance_calls); }
void cleanup_stale_dns_cache(void) { InterlockedIncrement(&maintenance_calls); }
DWORD fixture_wait(HANDLE handle,DWORD timeout) { SetEvent(wait_entered);return WaitForSingleObject(handle,timeout); }
#define WaitForSingleObject fixture_wait
'''+worker+'''#undef WaitForSingleObject
int checks;
void check(BOOL value,const char *name) { if(!value) { printf("FAIL %s\\n",name);ExitProcess(1); }checks++; }
typedef BOOL (*BeginPinned)(void);
int wmain(int argc,wchar_t **argv)
{
 wchar_t executable[MAX_PATH],dll[MAX_PATH];GetModuleFileNameW(NULL,executable,MAX_PATH);
 wcscpy_s(dll,MAX_PATH,executable);wchar_t *slash=wcsrchr(dll,L'\\\\');wcscpy_s(slash+1,MAX_PATH-(slash+1-dll),L"LifecycleFixture.dll");
 if(argc==2 && wcscmp(argv[1],L"--exit-while-locked")==0)
 {
  HMODULE module=LoadLibraryExW(dll,NULL,LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR|LOAD_LIBRARY_SEARCH_SYSTEM32);
  if(!module) return 10;BeginPinned begin=(BeginPinned)GetProcAddress(module,"FixtureBeginPinned");
  if(!begin || !begin()) return 11;
  FreeLibrary(module); // PIN keeps active code loaded; process termination is a distinct path.
  if(GetModuleHandleW(L"LifecycleFixture.dll")==NULL) return 12;
  return 0; // OS kills the lock holder; actual DllMain must not attempt that lock.
 }
 check(ld_relay_v4(TRUE,1,2,34010,50000,34010,TRUE,FALSE,FALSE,1,2,50000,443),"IPv4 valid relay tuple");
 check(!ld_relay_v4(FALSE,2,1,34010,50000,34010,TRUE,FALSE,FALSE,1,2,50000,443),"IPv4 remote port34010 response unchanged");
 check(!ld_relay_v4(TRUE,1,2,34010,50000,34010,FALSE,FALSE,FALSE,1,2,50000,443),"IPv4 missing record");
 check(!ld_relay_v4(TRUE,9,2,34010,50000,34010,TRUE,FALSE,FALSE,1,2,50000,443),"IPv4 wrong local address");
 check(!ld_relay_v4(TRUE,1,9,34010,50000,34010,TRUE,FALSE,FALSE,1,2,50000,443),"IPv4 wrong remote address");
 check(!ld_relay_v4(TRUE,1,2,34010,50001,34010,TRUE,FALSE,FALSE,1,2,50000,443),"IPv4 wrong client port");
 check(!ld_relay_v4(TRUE,1,2,34010,50000,34010,TRUE,TRUE,FALSE,1,2,50000,443),"IPv4 UDP record rejected");
 check(!ld_relay_v4(TRUE,1,2,34010,50000,34010,TRUE,FALSE,TRUE,1,2,50000,443),"IPv4 IPv6 record rejected");
 check(!ld_relay_v4(TRUE,1,2,34010,50000,34010,TRUE,FALSE,FALSE,1,2,50000,80),"IPv4 wrong original port");
 UINT8 local[16]={1},remote[16]={2},wrong[16]={3};
 check(ld_relay_v6(TRUE,local,remote,34010,50000,34010,TRUE,FALSE,TRUE,local,remote,50000,443),"IPv6 valid relay tuple");
 check(!ld_relay_v6(FALSE,remote,local,34010,50000,34010,TRUE,FALSE,TRUE,local,remote,50000,443),"IPv6 remote port34010 response unchanged");
 check(!ld_relay_v6(TRUE,local,remote,34010,50000,34010,FALSE,FALSE,TRUE,local,remote,50000,443),"IPv6 missing record");
 check(!ld_relay_v6(TRUE,wrong,remote,34010,50000,34010,TRUE,FALSE,TRUE,local,remote,50000,443),"IPv6 wrong local address");
 check(!ld_relay_v6(TRUE,local,wrong,34010,50000,34010,TRUE,FALSE,TRUE,local,remote,50000,443),"IPv6 wrong remote address");
 check(!ld_relay_v6(TRUE,local,remote,34010,50001,34010,TRUE,FALSE,TRUE,local,remote,50000,443),"IPv6 wrong client port");
 check(!ld_relay_v6(TRUE,local,remote,34010,50000,34010,TRUE,TRUE,TRUE,local,remote,50000,443),"IPv6 UDP rejected");
 for(int i=0;i<8;i++)
 {
  running=TRUE;maintenance_calls=0;g_cleanup_stop=CreateEvent(NULL,TRUE,FALSE,NULL);wait_entered=CreateEvent(NULL,TRUE,FALSE,NULL);
  HANDLE worker=CreateThread(NULL,0,cleanup_worker,NULL,0,NULL);check(worker!=NULL,"actual cleanup worker started");
  check(WaitForSingleObject(wait_entered,1000)==WAIT_OBJECT_0,"actual worker entered 30-second wait");
  running=FALSE;SetEvent(g_cleanup_stop);
  check(WaitForSingleObject(worker,1000)==WAIT_OBJECT_0,"actual cleanup worker wakes within original stop budget");
  check(maintenance_calls==0,"stop does not run maintenance");CloseHandle(worker);CloseHandle(wait_entered);CloseHandle(g_cleanup_stop);
 }
 HMODULE fixture=LoadLibraryExW(dll,NULL,LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR|LOAD_LIBRARY_SEARCH_SYSTEM32);check(fixture!=NULL,"own no-driver lifecycle fixture loaded");
 check(FreeLibrary(fixture)&&GetModuleHandleW(L"LifecycleFixture.dll")==NULL,"explicit pre-start unload completes");
 wchar_t command[MAX_PATH+40];swprintf_s(command,MAX_PATH+40,L"\\\"%s\\\" --exit-while-locked",executable);
 STARTUPINFOW startup={0};PROCESS_INFORMATION child={0};startup.cb=sizeof(startup);
 check(CreateProcessW(executable,command,NULL,NULL,FALSE,CREATE_NO_WINDOW,NULL,NULL,&startup,&child),"own lifecycle child started");
 DWORD ended=WaitForSingleObject(child.hProcess,3000),exit_code=99;
 if(ended!=WAIT_OBJECT_0) { printf("FAIL process-detach deadlock in owned fixture child\\n");TerminateProcess(child.hProcess,99);WaitForSingleObject(child.hProcess,1000);return 1; }
 GetExitCodeProcess(child.hProcess,&exit_code);CloseHandle(child.hThread);CloseHandle(child.hProcess);
 check(exit_code==0,"actual DllMain process exit avoids a held rules lock and pinned unload is safe");
 printf("PASS %d native assertions; actual cleanup_worker/DllMain extracted, own fixture only; no WinDivert or production DLL loaded, no sockets created.\\n",checks);return 0;
}
'''
(out/'NativeSafetyTests.c').write_text(main,encoding='utf-8')
(out/'fixture-provenance.txt').write_text('ProxyBridge.c SHA256 '+hashlib.sha256((src/'ProxyBridge.c').read_bytes()).hexdigest()+'\nld_safety.h SHA256 '+hashlib.sha256((src/'ld_safety.h').read_bytes()).hexdigest()+'\ncleanup_worker and DllMain copied verbatim from this production source. Fixture stubs all packet/driver functions.\n')
print('Generated no-driver native tests and fixture from actual production function bodies.')
