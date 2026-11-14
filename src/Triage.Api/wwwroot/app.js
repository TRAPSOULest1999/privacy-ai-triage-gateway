const $ = id => document.getElementById(id);
const samples = {
 billing: 'Hi, my invoice for account ACC-10482 has been billed twice. Please check the debit reconciliation. You can reach me at alex@example.com or 0821234567.',
 security: 'We detected unauthorized access and a possible token leak for subscriber SUB-20914. Contact security@example.com to investigate the breach urgently.',
 network: 'My SIM has no roaming service after provisioning. Subscriber SUB-30491 is affected. Please contact alex@example.com with an update.'
};
const ticket = $('ticket');
const sampleButtons = [...document.querySelectorAll('[data-sample]')];
let busy = false;
function count() { $('char-count').textContent = `${ticket.value.length.toLocaleString()} / 12,000`; }
function show(state) {
 ['empty', 'loading', 'result'].forEach(id => $(id).hidden = id !== state);
 $('comparison').hidden = state !== 'result';
 $('workspace').setAttribute('aria-busy', String(state === 'loading'));
}
function discardResult() {
 ['category','priority','decision-title','rationale','confidence','entities','sop','destination','audit','original','sanitized','entity-tags','redaction-summary','comparison-note'].forEach(id => $(id).replaceChildren());
 show('empty'); $('result-state').textContent = 'AWAITING INPUT'; $('result-state').className = 'result-state';
}
function selectSample(key) { sampleButtons.forEach(button => button.setAttribute('aria-pressed', String(button.dataset.sample === key))); }
function highlightRedactions(text) {
 // Render all input as text nodes. Only known placeholder tokens become marks.
 const parts = text.split(/(<(?:SA_ID_NUMBER|EMAIL_ADDRESS|PHONE_NUMBER|ACCOUNT_REF|PAYMENT_CARD|PERSON|LOCATION|ORGANIZATION|IP_ADDRESS|URL|DATE_TIME|NRP|IBAN_CODE|CREDIT_CARD|US_SSN)>)/g);
 $('sanitized').replaceChildren(...parts.filter(Boolean).map(part => {
  if (/^<[A-Z_]+>$/.test(part)) { const mark = document.createElement('mark'); mark.textContent = part; return mark; }
  return document.createTextNode(part);
 }));
}
ticket.addEventListener('input', () => { count(); ticket.setCustomValidity(''); selectSample(null); discardResult(); $('error').hidden = true; });
sampleButtons.forEach(button => { button.setAttribute('aria-pressed', 'false'); button.addEventListener('click', () => {
 if (busy) return;
 ticket.value = samples[button.dataset.sample]; ticket.setCustomValidity(''); count(); selectSample(button.dataset.sample);
 $('triage-form').requestSubmit();
}); });
$('clear').addEventListener('click', () => {
 if (busy) return;
 ticket.value = ''; ticket.setCustomValidity(''); count(); selectSample(null); discardResult(); $('error').hidden = true; $('error').textContent = ''; $('announcement').textContent = 'Session cleared.'; ticket.focus();
});
$('triage-form').addEventListener('submit', async event => {
 event.preventDefault(); if (busy) return;
 if (!ticket.value.trim()) { ticket.setCustomValidity('Please enter a ticket to evaluate.'); ticket.reportValidity(); return; }
 const submittedText = ticket.value;
 busy = true; $('error').hidden = true; $('submit').disabled = true; $('clear').disabled = true; ticket.readOnly = true; $('channel').disabled = true;
 sampleButtons.forEach(button => button.disabled = true);
 discardResult(); show('loading'); $('result-state').textContent = 'PROCESSING'; $('announcement').textContent = 'Evaluating ticket.';
 const controller = new AbortController(); const timeout = setTimeout(() => controller.abort(), 30000);
 try {
  const response = await fetch('/api/v1/triage', { method:'POST', headers:{'Content-Type':'application/json'}, body:JSON.stringify({ sourceChannel:$('channel').value, rawTicketText:submittedText }), signal:controller.signal });
  if (!response.ok) throw new Error(`The gateway could not evaluate this ticket (HTTP ${response.status}). Please try again.`);
  const data = await response.json();
  if (!Number.isFinite(data.confidence) || typeof data.redactedTicketText !== 'string' || !Number.isInteger(data.piiEntitiesScrubbed)) throw new Error('The gateway returned an unexpected response. Please try again.');
  $('category').textContent = (data.category || 'Unclassified').replace(/([a-z])([A-Z])/g,'$1 $2');
  $('priority').textContent = data.priority;
  $('decision-title').textContent = data.routedToHumanReview ? 'A human perspective is needed.' : 'Ready for the right team.';
  $('rationale').textContent = data.rationale;
  $('confidence').textContent = `${Math.round(data.confidence * 100)}%`;
  $('entities').textContent = data.piiEntitiesScrubbed;
  $('sop').textContent = data.matchedSopCode || 'None';
  $('original').textContent = submittedText;
  highlightRedactions(data.redactedTicketText);
  $('redaction-summary').textContent = `${data.piiEntitiesScrubbed} ${data.piiEntitiesScrubbed === 1 ? 'entity' : 'entities'} scrubbed`;
  $('comparison-note').textContent = data.piiEntitiesScrubbed > 0 ? 'Highlighted placeholders replace detected identifiers. The surrounding wording is preserved.' : 'No supported sensitive entities were detected. Unchanged text does not guarantee that the ticket contains no personal information.';
  $('destination').textContent = data.routingDestination;
  $('audit').textContent = `Audit ID: ${data.auditId}`;
  $('entity-tags').replaceChildren(...(data.detectedEntityTypes || []).map(type => { const tag = document.createElement('span'); tag.textContent = type.replaceAll('_', ' '); return tag; }));
  $('result-state').textContent = data.routedToHumanReview ? 'HUMAN REVIEW' : 'EVALUATED';
  $('result-state').className = `result-state ${data.routedToHumanReview ? 'review' : 'complete'}`;
  show('result'); $('announcement').textContent = `Triage complete. ${data.piiEntitiesScrubbed} entities scrubbed. ${$('decision-title').textContent}`;
  $('comparison-heading').focus({preventScroll:true});
  $('comparison').scrollIntoView({behavior:window.matchMedia('(prefers-reduced-motion: reduce)').matches ? 'instant' : 'smooth', block:'nearest'});
 } catch (error) {
  discardResult(); $('result-state').textContent = 'TRY AGAIN'; $('error').textContent = error.name === 'AbortError' ? 'The gateway took too long to respond. Please try again.' : (error instanceof TypeError ? 'Could not connect to the gateway. Check that the application is running and try again.' : error.message); $('error').hidden = false; $('announcement').textContent = 'Evaluation failed. See the message below the ticket.';
 } finally {
  clearTimeout(timeout); busy = false; $('submit').disabled = false; $('clear').disabled = false; ticket.readOnly = false; $('channel').disabled = false;
  sampleButtons.forEach(button => button.disabled = false);
 }
});
fetch('/api/v1/health', {signal:AbortSignal.timeout(5000)}).then(response => { if (!response.ok) throw new Error(); return response.json(); }).then(data => {
 if (data.status !== 'healthy') throw new Error(); $('health').className = 'status online'; $('health').replaceChildren(document.createElement('i'), document.createTextNode('API online')); $('health').title = 'API reachable. This does not verify Ollama or Presidio availability.';
}).catch(() => { $('health').className = 'status offline'; $('health').replaceChildren(document.createElement('i'), document.createTextNode('API unavailable')); });
