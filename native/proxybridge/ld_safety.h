#ifndef LD_SAFETY_H
#define LD_SAFETY_H
#include <windows.h>
#include <string.h>
static BOOL ld_relay_v4(BOOL outbound, UINT32 src, UINT32 dst, UINT16 sp, UINT16 dp,
    UINT16 relay, BOOL tracked, BOOL udp, BOOL ipv6, UINT32 client, UINT32 remote,
    UINT16 client_port, UINT16 remote_port)
{
    return outbound && tracked && !udp && !ipv6 && sp == relay && dp == client_port
        && remote_port == 443 && src == client && dst == remote;
}
static BOOL ld_relay_v6(BOOL outbound, const UINT8 src[16], const UINT8 dst[16], UINT16 sp, UINT16 dp,
    UINT16 relay, BOOL tracked, BOOL udp, BOOL ipv6, const UINT8 client[16], const UINT8 remote[16],
    UINT16 client_port, UINT16 remote_port)
{
    return outbound && tracked && !udp && ipv6 && sp == relay && dp == client_port
        && remote_port == 443 && memcmp(src,client,16)==0 && memcmp(dst,remote,16)==0;
}
static BOOL ld_pin_module(const void *address)
{
    HMODULE module;
    return GetModuleHandleExA(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_PIN,
        (LPCSTR)address,&module);
}
#endif
