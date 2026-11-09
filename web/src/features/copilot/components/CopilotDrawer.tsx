import { useRef, useState, type FormEvent } from 'react';
import { useUiStore } from '@/shared/store/uiStore';
import { streamAnswer, type Citation } from '../api/streamAnswer';

export function CopilotDrawer() {
  const { copilotOpen, copilotClaimId, closeCopilot } = useUiStore();
  const [answer, setAnswer] = useState('');
  const [citations, setCitations] = useState<Citation[]>([]);
  const [status, setStatus] = useState<'idle' | 'streaming' | 'error'>('idle');
  const abortRef = useRef<AbortController | null>(null);

  async function ask(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const question = String(new FormData(event.currentTarget).get('question') ?? '').trim();
    if (!question) return;

    abortRef.current?.abort(); // a new question cancels the in-flight stream
    const controller = new AbortController();
    abortRef.current = controller;
    setAnswer('');
    setCitations([]);
    setStatus('streaming');

    try {
      for await (const evt of streamAnswer(question, copilotClaimId, controller.signal)) {
        if (evt.type === 'citations') setCitations(evt.citations);
        if (evt.type === 'delta') setAnswer((a) => a + evt.text);
      }
      setStatus('idle');
    } catch (error) {
      if (!(error instanceof DOMException && error.name === 'AbortError')) setStatus('error');
    }
  }

  if (!copilotOpen) return null;

  return (
    <aside className="drawer" aria-label="Passenger rights copilot">
      <header className="row space-between">
        <h2>Rights copilot</h2>
        <button className="secondary" onClick={() => { abortRef.current?.abort(); closeCopilot(); }} aria-label="Close copilot">✕</button>
      </header>
      {copilotClaimId && <p className="muted">Answering in the context of your claim.</p>}
      <form onSubmit={ask} className="row">
        <input name="question" placeholder="Am I owed money for my cancelled flight?" aria-label="Your question" maxLength={1000} />
        <button type="submit" disabled={status === 'streaming'}>Ask</button>
      </form>

      <div aria-live="polite" className="answer">
        {answer || (status === 'streaming' ? 'Thinking…' : '')}
      </div>
      {status === 'error' && <p role="alert">The copilot is unavailable right now. Please try again.</p>}

      {citations.length > 0 && (
        <details open>
          <summary>Sources</summary>
          <ol>
            {citations.map((c) => (
              <li key={c.number}>[{c.number}] {c.title} — {c.section}</li>
            ))}
          </ol>
        </details>
      )}
      <p className="muted small">AI-generated explanation. Compensation amounts are calculated by Reroute's rules engine, not the AI.</p>
    </aside>
  );
}
