// Controlled replay against unchanged vendor modules; no browser or native UI.
import {readFile} from 'node:fs/promises';
import {pathToFileURL} from 'node:url';
import {join} from 'node:path';
import vm from 'node:vm';
import assert from 'node:assert/strict';
import {createHash} from 'node:crypto';
const bin=process.argv[2];
const root=join(bin,'node_modules/@oai/cua/dist/lib/js/oai_js_cua/src/tinysky_alt');
const {bind_tab}=await import(pathToFileURL(join(root,'bind_tab.js')));
const {create_browser_api}=await import(pathToFileURL(join(root,'create_browser_api.js')));
const source=await readFile(join(bin,'node_modules/@oai/browser-desktop/scripts/browser-service.mjs'),'utf8');
// Use the actual metadata flag function in isolation, not a reimplementation.
const profiles={
 'be80dd92f34c1c912f2061bcedbc366fd422190f80077e90a7b63c873f6ff3f3':{fn:'pre',next:'GL',key:'OT',gate:'Av'},
 '44594ea0a8238c09dfb46e28acdd610e5053a05011047e1c78fb98f210195a4e':{fn:'hre',next:'YL',key:'UT',gate:'Rv'}
};
const profile=profiles[createHash('sha256').update(source).digest('hex')];
assert(profile,'unreviewed vendor browser service');
const start=source.indexOf(`function ${profile.fn}(t,e){`);
const end=source.indexOf(`function ${profile.next}(`,start);
assert(start>=0 && end>start && end-start<2500,'reviewed metadata function located');
const context=vm.createContext({Nt:(config,key)=>config.env[key],[profile.key]:'BROWSER_USE_TINYSKY_ENABLED',[profile.gate]:()=>false});
const applyFlag=new vm.Script(source.slice(start,end)+`;${profile.fn}`).runInContext(context);
let checks=1;
if(profile.fn==='hre') {
 const parserStart=source.indexOf('function cL({apiManifest:');
 const parserEnd=source.indexOf('c();function uL(',parserStart);
 assert(parserStart>=0 && parserEnd>parserStart);
 const disabledMembers=new vm.Script(source.slice(parserStart,parserEnd)+';cL').runInNewContext();
 const metadata=applyFlag({env:{BROWSER_USE_TINYSKY_ENABLED:'1'}},{type:'iab',apiSupportOverrides:{}});
 const disabled=disabledMembers({apiManifest:{interfaces:{Tab:{ax:{},screenshot:{}}}},browserInfo:metadata,disabledMemberIds:new Set(['Tab.ax'])});
 assert.equal(disabled.has('Tab.ax'),true);assert.equal(disabled.has('Tab.screenshot'),false);checks+=2;
 const tab=bind_tab({id:'launch-policy',playwright:{domSnapshot:async()=>'persistent fallback'},screenshot:async()=>new Uint8Array([4]),
   ...(!disabled.has('Tab.ax')?{ax:{get:()=>new Promise(()=>{})}}:{})});
 assert.equal(await tab.getAXState({emit:false}),'persistent fallback');
 assert.deepEqual(await tab.getScreenshot({emit:false}),new Uint8Array([4]));checks+=2;
}
for(const flag of ['1','0']) {
 const metadata=applyFlag({env:{BROWSER_USE_TINYSKY_ENABLED:flag}},{type:'iab',apiSupportOverrides:{'Browser.user':false}});
 assert.equal(Boolean(metadata.apiSupportOverrides['Tab.ax']),flag==='1'); checks++;
 assert.equal(metadata.apiSupportOverrides['Browser.user'],false);checks++;
 let dom=0,shot=0,ax=0;
 const tab=bind_tab({id:'fixture',playwright:{domSnapshot:async()=>{dom++;return 'fixture DOM';}},screenshot:async()=>{shot++;return new Uint8Array([1,2,3]);},
  ...(metadata.apiSupportOverrides['Tab.ax']?{ax:{get:()=>{ax++;return new Promise(()=>{});}}}:{})});
 const browser={browserId:'iab',tabs:{list:async()=>[{id:'fixture',url:'https://fixture.invalid/'}],get:async()=>tab}};
 const api=create_browser_api({get:async()=>browser},{load_browser_documentation:async()=>{},emit:async()=>{}});
 const result=await Promise.race([api.getTab('fixture',{browser:'iab'}),new Promise(resolve=>setTimeout(()=>resolve('timeout'),30))]);
 if(flag==='1') {assert.equal(result,'timeout');assert.equal(ax,1);assert.equal(dom,0);checks+=3;}
 else {
  assert.equal(result,tab);assert.equal(dom,1);assert.equal(ax,0);checks+=3;
  assert.equal(await result.getAXState({emit:false}),'fixture DOM');checks++;
  assert.deepEqual(await result.getScreenshot({emit:false}),new Uint8Array([1,2,3]));assert.equal(shot,1);checks+=2;
  const combined=await result.getAXStateAndScreenshot({emit:false});
  assert.equal(combined.state,'fixture DOM');assert.deepEqual(combined.screenshot,new Uint8Array([1,2,3]));checks+=2;
 }
}
console.log(`PASS ${checks} checks: actual vendor flag parser + ordinary getTab fallback + reads/screenshots; isolated fake tab only.`);
