// Dependency-free interaction checks. Run: node --test tests/Frontend/portfolio.test.cjs
const {test} = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const path = require('node:path');
const root = path.resolve(__dirname, '../../src/Triage.Api/wwwroot');
function setup(decision, fail = false) {
 const elements = new Map();
 class Element {
  constructor(id = '') { this.id=id; this.textContent=''; this.value=''; this.listeners={}; this.children=[]; this.attrs={}; this.hidden=false; this.dataset={}; }
  addEventListener(event, fn) { this.listeners[event]=fn; }
  replaceChildren(...children) { this.children=children; this.textContent=children.map(c=>c.textContent).join(''); }
  setAttribute(key, value) { this.attrs[key]=value; }
  setCustomValidity(value) { this.validity=value; }
  reportValidity() {}
  focus() {}
  scrollIntoView() {}
  requestSubmit() { this.pending=this.listeners.submit({preventDefault(){}}); }
 }
 const html=fs.readFileSync(path.join(root,'index.html'),'utf8');
 for (const [,id] of html.matchAll(/id="([^"]+)"/g)) { assert.ok(!elements.has(id), `duplicate id ${id}`); elements.set(id,new Element(id)); }
 elements.get('channel').value='email';
 const buttons=['billing','security','network'].map(key=> { const b=new Element(); b.dataset.sample=key; return b; });
 const requests=[];
 const context={document:{getElementById:id=> { assert.ok(elements.has(id),`missing ${id}`); return elements.get(id); },querySelectorAll:()=>buttons,createElement:tag=> {const e=new Element(); e.tag=tag; return e;},createTextNode:text=>({textContent:text})},window:{matchMedia:()=>({matches:true})},AbortController,AbortSignal,setTimeout,clearTimeout,fetch:async(url,options)=>{
  if(url.includes('health')) return {ok:true,json:async()=>({status:'healthy'})};
  requests.push(JSON.parse(options.body));
  if(fail) throw new TypeError('network unavailable');
  return {ok:true,json:async()=>decision};
 }};
 vm.runInNewContext(fs.readFileSync(path.join(root,'app.js'),'utf8'),context);
 return {get:id=>elements.get(id),buttons,requests};
}
const response={confidence:.89,redactedTicketText:'Email <EMAIL_ADDRESS>. <script>alert(1)</script>',piiEntitiesScrubbed:1,category:'BillingReconciliation',priority:'P2',routedToHumanReview:false,rationale:'Billing workflow.',matchedSopCode:'SOP-BILL-204',routingDestination:'Auto-Queue:BillingReconciliation',auditId:'test-audit',detectedEntityTypes:['EMAIL_ADDRESS']};
test('one-click demo posts real request and safely highlights only replacement tokens',async()=>{
 const ui=setup(response); ui.buttons[0].listeners.click(); await ui.get('triage-form').pending;
 assert.equal(ui.requests.length,1); assert.match(ui.requests[0].rawTicketText,/alex@example.com/);
 assert.equal(ui.get('original').textContent,ui.requests[0].rawTicketText);
 assert.equal(ui.get('comparison').hidden,false);
 assert.equal(ui.get('sanitized').textContent,response.redactedTicketText);
 assert.equal(ui.get('sanitized').children.filter(c=>c.tag==='mark').length,1);
 assert.equal(ui.get('sanitized').children.some(c=>c.tag==='script'),false);
 assert.equal(ui.get('submit').disabled,false);
});
test('clear removes original, redacted output, metadata, and sample selection',async()=>{
 const ui=setup(response); ui.buttons[0].listeners.click(); await ui.get('triage-form').pending; ui.get('clear').listeners.click();
 for(const id of ['original','sanitized','audit','ticket']) assert.equal(id==='ticket'?ui.get(id).value:ui.get(id).textContent,'');
 assert.equal(ui.get('comparison').hidden,true);
 assert.ok(ui.buttons.every(b=>b.attrs['aria-pressed']==='false'));
});
test('editing input invalidates previous results',async()=>{
 const ui=setup(response); ui.buttons[0].listeners.click(); await ui.get('triage-form').pending;
 ui.get('ticket').value='A different ticket'; ui.get('ticket').listeners.input();
 assert.equal(ui.get('comparison').hidden,true); assert.equal(ui.get('original').textContent,'');
});
test('network failure is recoverable and exposes no stale comparison',async()=>{
 const ui=setup(response,true); ui.buttons[1].listeners.click(); await ui.get('triage-form').pending;
 assert.equal(ui.get('error').hidden,false); assert.equal(ui.get('comparison').hidden,true); assert.equal(ui.get('submit').disabled,false);
});
test('zero detections are explained; security decisions show human review',async()=>{
 const ui=setup({...response,piiEntitiesScrubbed:0,redactedTicketText:'A security incident',routedToHumanReview:true});
 ui.buttons[1].listeners.click(); await ui.get('triage-form').pending;
 assert.match(ui.get('comparison-note').textContent,/does not guarantee/); assert.equal(ui.get('result-state').textContent,'HUMAN REVIEW');
});
