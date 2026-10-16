#include "pb_tls.h"
#include <windows.h>
#include <stdio.h>
#include <string.h>
#include <stdlib.h>

static int checks;
#define CHECK(x) do { if (!(x)) { fprintf(stderr,"FAIL line %d: %s\n",__LINE__,#x); exit(1); } checks++; } while(0)
static void put16(unsigned char *b, size_t n) { b[0]=(unsigned char)(n>>8); b[1]=(unsigned char)n; }
static size_t client_hello(unsigned char *b,const char *host)
{
    size_t n=strlen(host),p=9;
    b[0]=22;b[1]=3;b[2]=1;
    b[5]=1;b[6]=0;
    b[p++]=3;b[p++]=3;memset(b+p,0,32);p+=32;
    b[p++]=0;put16(b+p,2);p+=2;b[p++]=0x13;b[p++]=1;
    b[p++]=1;b[p++]=0;put16(b+p,n+9);p+=2;
    put16(b+p,0);p+=2;put16(b+p,n+5);p+=2;put16(b+p,n+3);p+=2;
    b[p++]=0;put16(b+p,n);p+=2;memcpy(b+p,host,n);p+=n;
    put16(b+3,p-5);put16(b+7,p-9);
    return p;
}
static void pair(SOCKET *a,SOCKET *b)
{
    SOCKET listener=socket(AF_INET,SOCK_STREAM,0);CHECK(listener!=INVALID_SOCKET);
    struct sockaddr_in address;memset(&address,0,sizeof(address));address.sin_family=AF_INET;address.sin_addr.s_addr=htonl(INADDR_LOOPBACK);
    CHECK(bind(listener,(struct sockaddr*)&address,sizeof(address))==0);CHECK(listen(listener,1)==0);
    int len=sizeof(address);CHECK(getsockname(listener,(struct sockaddr*)&address,&len)==0);
    *a=socket(AF_INET,SOCK_STREAM,0);CHECK(connect(*a,(struct sockaddr*)&address,len)==0);
    *b=accept(listener,NULL,NULL);CHECK(*b!=INVALID_SOCKET);closesocket(listener);
    DWORD timeout=2000;setsockopt(*a,SOL_SOCKET,SO_RCVTIMEO,(char*)&timeout,sizeof(timeout));setsockopt(*b,SOL_SOCKET,SO_RCVTIMEO,(char*)&timeout,sizeof(timeout));
}
typedef struct {SOCKET socket;unsigned char bytes[1024];size_t size;int status;int matched;} Sender;
static DWORD WINAPI fragmented_hello(LPVOID arg)
{
    Sender *s=(Sender*)arg;send(s->socket,(char*)s->bytes,3,0);Sleep(30);send(s->socket,(char*)s->bytes+3,(int)s->size-3,0);return 0;
}
static DWORD WINAPI proxy_reply(LPVOID arg)
{
    Sender *s=(Sender*)arg;char request[1024];int used=0;
    while(used<(int)sizeof(request)-1){if(recv(s->socket,request+used,1,0)!=1)return 1;used++;if(used>=4&&!memcmp(request+used-4,"\r\n\r\n",4))break;}
    request[used]=0;s->matched=!strcmp(request,"CONNECT codex-cloud-backend.chatgpt.com:443 HTTP/1.1\r\nHost: codex-cloud-backend.chatgpt.com:443\r\nProxy-Connection: keep-alive\r\n\r\n");
    char reply[128];sprintf_s(reply,sizeof(reply),"HTTP/1.1 %d Test\r\n\r\nZ",s->status);
    send(s->socket,reply,7,0);Sleep(10);send(s->socket,reply+7,(int)strlen(reply)-7,0);return 0;
}
static void tests(void)
{
    unsigned char bytes[1024],split[1100];char host[254];
    size_t size=client_hello(bytes,"CoDeX-cloud-backend.ChatGPT.com");
    CHECK(ld_tls_server_name(bytes,size,host,sizeof(host))==1);CHECK(!strcmp(host,"codex-cloud-backend.chatgpt.com"));CHECK(ld_tls_is_openai_host(host));
    for(size_t i=0;i<size;i++)CHECK(ld_tls_server_name(bytes,i,host,sizeof(host))==-1);
    for(size_t part=1;part<size-5;part++){
        memcpy(split,bytes,5);put16(split+3,part);memcpy(split+5,bytes+5,part);
        memcpy(split+5+part,bytes,5);put16(split+8+part,size-5-part);memcpy(split+10+part,bytes+5+part,size-5-part);
        CHECK(ld_tls_server_name(split,size+5,host,sizeof(host))==1);CHECK(!strcmp(host,"codex-cloud-backend.chatgpt.com"));
    }
    const char *invalid[]={"chatgpt.com\r\nX: y","-chatgpt.com","chatgpt..com","chatgpt.com-","chatgpt.com.",""};
    for(size_t i=0;i<sizeof(invalid)/sizeof(invalid[0]);i++){size=client_hello(bytes,invalid[i]);CHECK(ld_tls_server_name(bytes,size,host,sizeof(host))==0);CHECK(host[0]==0);}
    CHECK(!ld_tls_is_openai_host("chatgpt.com.evil"));CHECK(!ld_tls_is_openai_host("notchatgpt.com"));CHECK(!ld_tls_is_openai_host("localhost"));CHECK(!ld_tls_is_openai_host("127.0.0.1"));CHECK(!ld_tls_is_openai_host("example.com"));CHECK(ld_tls_is_openai_host("api.openai.com"));
    size=client_hello(bytes,"chatgpt.com");CHECK(ld_tls_server_name(bytes,size,host,4)==0);
    bytes[0]=23;CHECK(ld_tls_server_name(bytes,size,host,sizeof(host))==0);bytes[0]=22;
    bytes[6]=1;CHECK(ld_tls_server_name(bytes,size,host,sizeof(host))==0);
    unsigned int random=7;for(int i=0;i<3000;i++){for(size_t j=0;j<sizeof(bytes);j++){random=random*1664525u+1013904223u;bytes[j]=(unsigned char)(random>>24);}int rc=ld_tls_server_name(bytes,(size_t)(i%1024),host,sizeof(host));CHECK(rc>=-1&&rc<=1);}
    SOCKET a,b;pair(&a,&b);Sender sender={0};sender.socket=a;sender.size=client_hello(sender.bytes,"chatgpt.com");
    HANDLE thread=CreateThread(NULL,0,fragmented_hello,&sender,0,NULL);CHECK(thread!=NULL);
    CHECK(ld_tls_peek_server_name(b,host,sizeof(host),500));CHECK(!strcmp(host,"chatgpt.com"));
    CHECK(WaitForSingleObject(thread,2000)==WAIT_OBJECT_0);CloseHandle(thread);
    CHECK(recv(b,(char*)bytes,(int)sender.size,MSG_WAITALL)==sender.size);CHECK(!memcmp(bytes,sender.bytes,sender.size));closesocket(a);closesocket(b);
    pair(&a,&b);ULONGLONG start=GetTickCount64();CHECK(!ld_tls_peek_server_name(b,host,sizeof(host),25));CHECK(GetTickCount64()-start<600);closesocket(a);closesocket(b);
    for(int code=200;code<=407;code+=207){pair(&a,&b);memset(&sender,0,sizeof(sender));sender.socket=b;sender.status=code;thread=CreateThread(NULL,0,proxy_reply,&sender,0,NULL);CHECK(thread!=NULL);int status;DWORD error;
        CHECK(ld_tls_http_connect(a,"codex-cloud-backend.chatgpt.com",443,&status,&error)==(code==200?0:-1));CHECK(status==code);CHECK(error==0);
        CHECK(WaitForSingleObject(thread,2000)==WAIT_OBJECT_0);CloseHandle(thread);CHECK(sender.matched);char sentinel;CHECK(recv(a,&sentinel,1,0)==1&&sentinel=='Z');closesocket(a);closesocket(b);
    }
    CHECK(ld_tls_http_connect(INVALID_SOCKET,"chatgpt.com\r\nHost:evil",443,NULL,NULL)==-1);
    printf("TLS/domain tunnel checks passed: %d\n",checks);
}
/* Driver-free integration fixture. It exercises the production peek/CONNECT
   helpers with a real TLS client, leaving TLS verification to that client. */
static int live_relay(int listen_port,int proxy_port)
{
    SOCKET listener=socket(AF_INET,SOCK_STREAM,0),client,upstream;struct sockaddr_in address;memset(&address,0,sizeof(address));address.sin_family=AF_INET;address.sin_addr.s_addr=htonl(INADDR_LOOPBACK);address.sin_port=htons((u_short)listen_port);
    if(bind(listener,(struct sockaddr*)&address,sizeof(address))||listen(listener,1))return 2;
    puts("READY");fflush(stdout);client=accept(listener,NULL,NULL);closesocket(listener);if(client==INVALID_SOCKET)return 3;
    char host[254];if(!ld_tls_peek_server_name(client,host,sizeof(host),2000)||!ld_tls_is_openai_host(host))return 4;
    upstream=socket(AF_INET,SOCK_STREAM,0);address.sin_port=htons((u_short)proxy_port);DWORD timeout=5000;setsockopt(upstream,SOL_SOCKET,SO_RCVTIMEO,(char*)&timeout,sizeof(timeout));
    if(connect(upstream,(struct sockaddr*)&address,sizeof(address)))return 5;int status;DWORD error;if(ld_tls_http_connect(upstream,host,443,&status,&error))return 6;
    printf("DOMAIN_TUNNEL status=%d\n",status);fflush(stdout);
    for(;;){fd_set reads;FD_ZERO(&reads);FD_SET(client,&reads);FD_SET(upstream,&reads);struct timeval wait={10,0};if(select(0,&reads,NULL,NULL,&wait)<=0)break;
        SOCKET sources[2]={client,upstream},destinations[2]={upstream,client};for(int i=0;i<2;i++)if(FD_ISSET(sources[i],&reads)){char buffer[16384];int n=recv(sources[i],buffer,sizeof(buffer),0);if(n<=0)goto done;int sent=0;while(sent<n){int k=send(destinations[i],buffer+sent,n-sent,0);if(k<=0)goto done;sent+=k;}}
    }
done:closesocket(client);closesocket(upstream);return 0;
}
int main(int argc,char **argv)
{
    WSADATA data;if(WSAStartup(MAKEWORD(2,2),&data))return 2;
    if(argc==4&&!strcmp(argv[1],"--relay")){int rc=live_relay(atoi(argv[2]),atoi(argv[3]));WSACleanup();return rc;}
    tests();WSACleanup();return 0;
}
