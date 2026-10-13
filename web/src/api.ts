export type Session = { token: string; username: string; role: "admin" | "user" };
export type Source = { Label: string; DocumentId: string; ChunkIndex: number; Similarity: number };
export type AgentStep = { Agent: string; Action: string; Detail: string };

export type StreamEvent =
  | { type: "conversation"; data: { Id: string; SearchQuestion: string } }
  | { type: "cache"; data: { Hit: boolean; Similarity: number; Question: string } }
  | { type: "sources"; data: Source[] }
  | { type: "step"; data: AgentStep }
  | { type: "token"; data: string }
  | { type: "done"; data: Record<string, never> };

export async function login(username: string, password: string): Promise<Session> {
  const res = await fetch("/auth/login", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ username, password }),
  });
  if (res.status === 401) throw new Error("Kullanıcı adı veya şifre hatalı.");
  if (!res.ok) throw new Error(`Giriş başarısız (${res.status}).`);
  const body = await res.json();
  return { token: body.token, username: body.username, role: body.role };
}

export async function* streamAnswer(
  path: "/chat/stream" | "/agent/stream",
  body: { question: string; conversationId: string | null },
  token: string,
  signal: AbortSignal,
): AsyncGenerator<StreamEvent> {
  const res = await fetch(path, {
    method: "POST",
    headers: { "Content-Type": "application/json", Authorization: `Bearer ${token}` },
    body: JSON.stringify(body),
    signal,
  });
  if (res.status === 401) throw new Error("Oturumun süresi doldu, tekrar giriş yap.");
  if (!res.ok || !res.body) throw new Error(`Sunucu ${res.status} döndürdü.`);

  const reader = res.body.getReader();
  const decoder = new TextDecoder();
  let buffer = "";
  while (true) {
    const { value, done } = await reader.read();
    if (done) return;
    buffer += decoder.decode(value, { stream: true });
    let end: number;
    while ((end = buffer.indexOf("\n\n")) >= 0) {
      const raw = buffer.slice(0, end);
      buffer = buffer.slice(end + 2);
      const type = /^event: (.*)$/m.exec(raw)?.[1];
      const data = /^data: (.*)$/m.exec(raw)?.[1];
      if (type && data) yield { type, data: JSON.parse(data) } as StreamEvent;
    }
  }
}

export async function uploadDocument(documentId: string, text: string, token: string): Promise<string> {
  const res = await fetch("/documents", {
    method: "POST",
    headers: { "Content-Type": "application/json", Authorization: `Bearer ${token}` },
    body: JSON.stringify({ documentId, text }),
  });
  if (res.status === 403) throw new Error("Doküman yüklemek için admin yetkisi gerekiyor.");
  if (!res.ok) throw new Error(`Yükleme başarısız (${res.status}).`);
  return `${documentId} kuyruğa alındı, Worker birkaç saniye içinde işleyecek.`;
}
