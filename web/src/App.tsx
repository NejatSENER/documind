import { useRef, useState, type FormEvent } from "react";
import { login, streamAnswer, uploadDocument, type AgentStep, type Session, type Source } from "./api";

export default function App() {
  const [session, setSession] = useState<Session | null>(null);
  return session ? <Chat session={session} onLogout={() => setSession(null)} /> : <Login onLogin={setSession} />;
}

function Login({ onLogin }: { onLogin: (s: Session) => void }) {
  const [username, setUsername] = useState("calisan");
  const [password, setPassword] = useState("");
  const [error, setError] = useState("");

  async function submit(e: FormEvent) {
    e.preventDefault();
    setError("");
    try { onLogin(await login(username, password)); } catch (err) { setError((err as Error).message); }
  }

  return (
    <main className="narrow">
      <h1>DocuMind</h1>
      <form className="panel" onSubmit={submit}>
        <label htmlFor="username">Kullanıcı adı</label>
        <input id="username" value={username} onChange={(e) => setUsername(e.target.value)} />
        <label htmlFor="password">Şifre</label>
        <input id="password" type="password" value={password} onChange={(e) => setPassword(e.target.value)} />
        <button type="submit">Giriş yap</button>
        {error && <p className="error">{error}</p>}
      </form>
    </main>
  );
}

function Chat({ session, onLogout }: { session: Session; onLogout: () => void }) {
  const [question, setQuestion] = useState("Yıllık iznim kaç gün?");
  const [answer, setAnswer] = useState("");
  const [sources, setSources] = useState<Source[]>([]);
  const [steps, setSteps] = useState<AgentStep[]>([]);
  const [status, setStatus] = useState("");
  const [agentMode, setAgentMode] = useState(false);
  const [busy, setBusy] = useState(false);
  const conversationId = useRef<string | null>(null);
  const controller = useRef<AbortController | null>(null);

  async function ask(e?: FormEvent) {
    e?.preventDefault();
    if (!question.trim() || busy) return;
    controller.current = new AbortController();
    setBusy(true); setAnswer(""); setSources([]); setSteps([]); setStatus("Aranıyor…");
    const started = performance.now();
    let first: number | null = null;

    try {
      const path = agentMode ? "/agent/stream" : "/chat/stream";
      const body = { question, conversationId: conversationId.current };
      for await (const ev of streamAnswer(path, body, session.token, controller.current.signal)) {
        if (ev.type === "conversation") conversationId.current = ev.data.Id;
        if (ev.type === "cache" && ev.data.Hit) setStatus(`Cache'ten geldi (${ev.data.Similarity.toFixed(3)} benzer).`);
        if (ev.type === "sources") setSources(ev.data);
        if (ev.type === "step") setSteps((s) => [...s, ev.data]);
        if (ev.type === "token") { first ??= performance.now(); setAnswer((a) => a + ev.data); }
        if (ev.type === "done" && first !== null)
          setStatus((s) => `${s.startsWith("Cache") ? s + " " : ""}İlk kelime ${((first! - started) / 1000).toFixed(1)} sn · toplam ${((performance.now() - started) / 1000).toFixed(1)} sn`);
      }
    } catch (err) {
      setStatus((err as Error).name === "AbortError" ? "Durduruldu." : (err as Error).message);
    } finally {
      setBusy(false);
    }
  }

  return (
    <main>
      <header className="row between">
        <h1>DocuMind</h1>
        <span className="muted">{session.username} ({session.role}) <button className="ghost" onClick={onLogout}>Çıkış</button></span>
      </header>

      <form className="panel" onSubmit={ask}>
        <label htmlFor="question">Soru</label>
        <textarea id="question" rows={2} value={question} onChange={(e) => setQuestion(e.target.value)} />
        <div className="row">
          <button type="submit" disabled={busy}>Sor</button>
          <button type="button" className="ghost" disabled={!busy} onClick={() => controller.current?.abort()}>Durdur</button>
          <button type="button" className="ghost" onClick={() => { conversationId.current = null; setStatus("Yeni sohbet başladı."); }}>Yeni sohbet</button>
          <label className="check"><input type="checkbox" checked={agentMode} onChange={(e) => setAgentMode(e.target.checked)} /> Agent modu</label>
        </div>
        <p className="muted">{status}</p>
      </form>

      <section className="panel" aria-live="polite">
        <label>Cevap</label>
        <p className="answer">{answer || <span className="muted">Henüz cevap yok.</span>}</p>
        {steps.length > 0 && <ol className="muted">{steps.map((s, i) => <li key={i}>{s.Agent} · {s.Action}: {s.Detail}</li>)}</ol>}
        <div className="chips">{sources.map((s) => <span key={s.Label} className="chip">{s.Label} · {s.DocumentId} · parça {s.ChunkIndex} · {s.Similarity.toFixed(3)}</span>)}</div>
      </section>

      {session.role === "admin" && <Upload token={session.token} />}
    </main>
  );
}

function Upload({ token }: { token: string }) {
  const [documentId, setDocumentId] = useState("yeni-dokuman.md");
  const [text, setText] = useState("");
  const [status, setStatus] = useState("");

  async function submit(e: FormEvent) {
    e.preventDefault();
    try { setStatus(await uploadDocument(documentId, text, token)); } catch (err) { setStatus((err as Error).message); }
  }

  return (
    <form className="panel" onSubmit={submit}>
      <label htmlFor="docId">Doküman yükle (admin)</label>
      <input id="docId" value={documentId} onChange={(e) => setDocumentId(e.target.value)} />
      <textarea rows={4} value={text} placeholder="Doküman metni" aria-label="Doküman metni" onChange={(e) => setText(e.target.value)} />
      <div className="row"><button type="submit" disabled={!text.trim()}>Yükle</button><span className="muted">{status}</span></div>
    </form>
  );
}
