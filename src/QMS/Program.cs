using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using CsHttp;
using System.Engine;

// ============================================================
// Queue Ticket System — Pure C# Console Application with SSE
// HTTP parsing/response: cshttp (engine/cshttp/)
// Printing: System.Drawing.Printing.PrintDocument
// Mobile: hybrid polling + SSE (server-side, client-driven switch)
// ============================================================

namespace System
{
    class Program
    {
        static AppConfig Config;

        static Dictionary<int, string> Departments = new Dictionary<int, string>();

        static Dictionary<int, int> NextTicketNumber = new Dictionary<int, int>();

        // Ordered queues per department; LinkedList for O(1) remove anywhere
        static Dictionary<int, LinkedList<int>> WaitingQueues = new Dictionary<int, LinkedList<int>>();

        // Ticket state lookup: ticketNumber -> info
        static Dictionary<int, TicketInfo> Tickets = new Dictionary<int, TicketInfo>();

        static List<QueueCall> CallHistory = new List<QueueCall>();
        static Dictionary<int, QueueCall> LatestPerDept = new Dictionary<int, QueueCall>();

        static List<SseDisplayClient> DisplayClients = new List<SseDisplayClient>();
        static List<SseMobileClient> MobileClients = new List<SseMobileClient>();
        static int SseEventId = 0;

        static object Lock = new object();

        static void Main(string[] args)
        {
            Config = AppConfig.Load("config.txt");

            for (int i = 0; i < Config.Departments.Count; i++)
            {
                int id = i + 1;
                Departments[id] = Config.Departments[i];
                NextTicketNumber[id] = id * 1000;
                WaitingQueues[id] = new LinkedList<int>();
            }
            Console.WriteLine($"[config] Loaded {Departments.Count} department(s).");

            string dbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tickets.db");
            TicketStore.Init(dbPath);
            HydrateFromStore();

            TcpListener listener = new TcpListener(IPAddress.Any, Config.HttpPort);
            listener.Start();

            Console.WriteLine($"Queue Ticket System started on port {Config.HttpPort}");
            Console.WriteLine($"Mobile:   http://localhost:{Config.HttpPort}/");
            Console.WriteLine($"Display:  http://localhost:{Config.HttpPort}/display");
            Console.WriteLine($"Terminal: http://localhost:{Config.HttpPort}/terminal");
            Console.WriteLine($"Operator: http://localhost:{Config.HttpPort}/operator");
            Console.WriteLine($"Printer:  {(string.IsNullOrEmpty(Config.PrinterName) ? "(Windows default)" : Config.PrinterName)}");
            Console.WriteLine();

            while (true)
            {
                TcpClient client = listener.AcceptTcpClient();
                Thread thread = new Thread(() => HandleClient(client));
                thread.IsBackground = true;
                thread.Start();
            }
        }

        // ============================================================
        // HTTP — read with cshttp, route, respond
        // ============================================================

        static void HandleClient(TcpClient client)
        {
            try
            {
                NetworkStream stream = client.GetStream();
                stream.ReadTimeout = 10000;

                byte[] requestBytes = ReadFullRequest(stream);
                if (requestBytes == null) return;

                ParseResult pr = HttpParser.ParseRequest(requestBytes);
                if (!pr.Success || pr.Request == null)
                {
                    byte[] err = HttpResponse.Status(400);
                    stream.Write(err, 0, err.Length);
                    stream.Flush();
                    return;
                }

                HttpRequestMessage req = pr.Request;
                string path = (req.Path ?? "/").ToLowerInvariant().TrimEnd('/');
                if (path == "") path = "/";

                Console.WriteLine($"{req.Method} {req.RequestTarget}");

                // SSE — long-lived, hand-rolled
                if (path == "/sse-display")
                {
                    HandleSseDisplay(client, stream);
                    return;
                }
                if (path == "/sse-mobile")
                {
                    int t = TryInt(req.QueryString["t"]);
                    int d = TryInt(req.QueryString["d"]);
                    HandleSseMobile(client, stream, t, d);
                    return;
                }

                byte[] respBytes;
                switch (path)
                {
                    case "/":
                        if (req.Method == "POST")
                        {
                            int deptId = TryInt(req.Form["department-id"]);
                            if (Departments.ContainsKey(deptId))
                            {
                                int ticket = RegisterTicket(deptId);
                                respBytes = HttpResponse.Redirect($"/m?t={ticket}&d={deptId}");
                                break;
                            }
                        }
                        respBytes = HtmlBytes(PageRoot());
                        break;
                    case "/display":
                        respBytes = HtmlBytes(PageDisplay());
                        break;
                    case "/terminal":
                        respBytes = HtmlBytes(PageTerminal(req));
                        break;
                    case "/operator":
                        respBytes = HtmlBytes(PageOperator(req));
                        break;
                    case "/m":
                    case "/mobile":
                        respBytes = HtmlBytes(PageMobile(req));
                        break;
                    case "/status":
                        respBytes = JsonBytes(BuildStatusJson(
                            TryInt(req.QueryString["t"]),
                            TryInt(req.QueryString["d"])));
                        break;
                    case "/sqlite-test":
                        respBytes = JsonBytes(RunSqliteRoundtrip());
                        break;
                    default:
                        respBytes = new HttpResponse(404)
                            .Header("Content-Type", "text/html; charset=utf-8")
                            .Header("Connection", "close")
                            .Body(PageNotFound()).ToBytes();
                        break;
                }
                stream.Write(respBytes, 0, respBytes.Length);
                stream.Flush();
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error: " + ex.Message);
            }
            finally
            {
                try { client.Close(); } catch { }
            }
        }

        // Read request bytes: headers up to \r\n\r\n, then Content-Length body.
        // Hands the full buffer to cshttp for proper parsing.
        static byte[] ReadFullRequest(NetworkStream stream)
        {
            using (var ms = new MemoryStream())
            {
                int prev3 = 0, prev2 = 0, prev1 = 0, current = 0;
                while (true)
                {
                    int b = stream.ReadByte();
                    if (b == -1) return ms.Length == 0 ? null : ms.ToArray();
                    ms.WriteByte((byte)b);
                    prev3 = prev2; prev2 = prev1; prev1 = current; current = b;
                    if (prev3 == '\r' && prev2 == '\n' && prev1 == '\r' && current == '\n')
                        break;
                }

                int contentLength = 0;
                string headerText = Encoding.ASCII.GetString(ms.ToArray());
                foreach (string line in headerText.Split(new[] { "\r\n" }, StringSplitOptions.None))
                {
                    if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                    {
                        int.TryParse(line.Substring(15).Trim(), out contentLength);
                        break;
                    }
                }

                if (contentLength > 0)
                {
                    if (contentLength > 1_000_000) return null;
                    byte[] buf = new byte[contentLength];
                    int total = 0;
                    while (total < contentLength)
                    {
                        int n = stream.Read(buf, total, contentLength - total);
                        if (n == 0) break;
                        total += n;
                    }
                    ms.Write(buf, 0, total);
                }
                return ms.ToArray();
            }
        }

        static byte[] HtmlBytes(string html) =>
            new HttpResponse(200)
                .Header("Content-Type", "text/html; charset=utf-8")
                .Header("Cache-Control", "no-cache")
                .Header("Connection", "close")
                .Body(html).ToBytes();

        static byte[] JsonBytes(string json) =>
            new HttpResponse(200)
                .Header("Content-Type", "application/json; charset=utf-8")
                .Header("Cache-Control", "no-cache")
                .Header("Connection", "close")
                .Body(json).ToBytes();

        // ============================================================
        // SSE — Display Board (TV)
        // ============================================================

        static void HandleSseDisplay(TcpClient client, NetworkStream stream)
        {
            try
            {
                WriteSseHeaders(stream);

                string init = $"id: {SseEventId}\nevent: display\ndata: {EncodeSse(RenderDisplayBoard())}\n\n";
                byte[] initBytes = Encoding.UTF8.GetBytes(init);
                stream.Write(initBytes, 0, initBytes.Length);
                stream.Flush();

                var sc = new SseDisplayClient { Client = client, Stream = stream };
                lock (Lock) DisplayClients.Add(sc);
                Console.WriteLine("SSE display connected. Total: " + DisplayClients.Count);

                while (true)
                {
                    Thread.Sleep(15000);
                    try
                    {
                        byte[] hb = Encoding.UTF8.GetBytes(": heartbeat\n\n");
                        stream.Write(hb, 0, hb.Length);
                        stream.Flush();
                    }
                    catch { break; }
                }
            }
            catch { }
            finally
            {
                lock (Lock) DisplayClients.RemoveAll(c => c.Client == client);
                try { client.Close(); } catch { }
                Console.WriteLine("SSE display disconnected. Total: " + DisplayClients.Count);
            }
        }

        static void BroadcastDisplay()
        {
            string html = RenderDisplayBoard();
            int eventId;
            List<SseDisplayClient> snapshot;
            lock (Lock)
            {
                SseEventId++;
                eventId = SseEventId;
                snapshot = new List<SseDisplayClient>(DisplayClients);
            }
            byte[] data = Encoding.UTF8.GetBytes($"id: {eventId}\nevent: display\ndata: {EncodeSse(html)}\n\n");
            var dead = new List<SseDisplayClient>();
            foreach (var c in snapshot)
            {
                try { c.Stream.Write(data, 0, data.Length); c.Stream.Flush(); }
                catch { dead.Add(c); }
            }
            if (dead.Count > 0)
            {
                lock (Lock)
                    foreach (var d in dead) { DisplayClients.Remove(d); try { d.Client.Close(); } catch { } }
            }
        }

        // ============================================================
        // SSE — Mobile (per-ticket)
        // ============================================================

        static void HandleSseMobile(TcpClient client, NetworkStream stream, int ticket, int dept)
        {
            try
            {
                WriteSseHeaders(stream);

                string snapshot = BuildStatusJson(ticket, dept);
                byte[] init = Encoding.UTF8.GetBytes($"event: status\ndata: {snapshot}\n\n");
                stream.Write(init, 0, init.Length);
                stream.Flush();

                var sc = new SseMobileClient
                {
                    Client = client,
                    Stream = stream,
                    Ticket = ticket,
                    Dept = dept
                };
                lock (Lock) MobileClients.Add(sc);
                Console.WriteLine($"SSE mobile connected (t={ticket}, d={dept}). Total: " + MobileClients.Count);

                while (true)
                {
                    Thread.Sleep(15000);
                    try
                    {
                        byte[] hb = Encoding.UTF8.GetBytes(": heartbeat\n\n");
                        stream.Write(hb, 0, hb.Length);
                        stream.Flush();
                    }
                    catch { break; }
                }
            }
            catch { }
            finally
            {
                lock (Lock) MobileClients.RemoveAll(c => c.Client == client);
                try { client.Close(); } catch { }
                Console.WriteLine("SSE mobile disconnected. Total: " + MobileClients.Count);
            }
        }

        static void BroadcastMobile(int deptId)
        {
            List<SseMobileClient> snapshot;
            lock (Lock)
                snapshot = MobileClients.Where(c => c.Dept == deptId).ToList();

            var dead = new List<SseMobileClient>();
            foreach (var c in snapshot)
            {
                try
                {
                    string json = BuildStatusJson(c.Ticket, c.Dept);
                    byte[] data = Encoding.UTF8.GetBytes($"event: status\ndata: {json}\n\n");
                    c.Stream.Write(data, 0, data.Length);
                    c.Stream.Flush();
                }
                catch { dead.Add(c); }
            }
            if (dead.Count > 0)
            {
                lock (Lock)
                    foreach (var d in dead) { MobileClients.Remove(d); try { d.Client.Close(); } catch { } }
            }
        }

        static void WriteSseHeaders(NetworkStream stream)
        {
            string h =
                "HTTP/1.1 200 OK\r\n" +
                "Content-Type: text/event-stream\r\n" +
                "Cache-Control: no-cache\r\n" +
                "Connection: keep-alive\r\n" +
                "X-Accel-Buffering: no\r\n" +
                "\r\n";
            byte[] hb = Encoding.UTF8.GetBytes(h);
            stream.Write(hb, 0, hb.Length);
            stream.Flush();
        }

        static string EncodeSse(string html) =>
            html.Replace("\r", "").Replace("\n", "&#10;");

        // ============================================================
        // Status JSON — used by both /status (poll) and /sse-mobile
        // ============================================================

        static string BuildStatusJson(int ticket, int dept)
        {
            lock (Lock)
            {
                int status = 0;        // 0=waiting, 1=called
                int position = -1;
                int counter = 0;
                int latest = 0;
                var upcoming = new List<int>();

                if (LatestPerDept.TryGetValue(dept, out var last))
                    latest = last.TicketNumber;

                if (Tickets.TryGetValue(ticket, out var info) && info.Dept == dept)
                {
                    if (info.State == TicketState.Called)
                    {
                        status = 1;
                        counter = info.Counter;
                    }
                    else if (WaitingQueues.TryGetValue(dept, out var q))
                    {
                        int idx = 0; bool found = false;
                        foreach (int n in q) { if (n == ticket) { found = true; break; } idx++; }
                        if (found) position = idx;
                    }
                }

                if (WaitingQueues.TryGetValue(dept, out var q2))
                    upcoming = q2.Take(5).ToList();

                var sb = new StringBuilder();
                sb.Append("{");
                sb.Append("\"status\":").Append(status).Append(",");
                sb.Append("\"ticket\":").Append(ticket).Append(",");
                sb.Append("\"dept\":").Append(dept).Append(",");
                sb.Append("\"position\":").Append(position).Append(",");
                sb.Append("\"counter\":").Append(counter).Append(",");
                sb.Append("\"latest\":").Append(latest).Append(",");
                sb.Append("\"upcoming\":[");
                for (int i = 0; i < upcoming.Count; i++)
                {
                    if (i > 0) sb.Append(",");
                    sb.Append(upcoming[i]);
                }
                sb.Append("],");
                sb.Append("\"eventId\":").Append(SseEventId);
                sb.Append("}");
                return sb.ToString();
            }
        }

        // ============================================================
        // Display Board HTML
        // ============================================================

        static string RenderDisplayBoard()
        {
            lock (Lock)
            {
                QueueCall latest = CallHistory.Count > 0 ? CallHistory[CallHistory.Count - 1] : null;

                var recent = new List<QueueCall>();
                for (int i = CallHistory.Count - 2; i >= 0 && recent.Count < 5; i--)
                    recent.Add(CallHistory[i]);

                string main;
                if (latest != null)
                {
                    main = $@"
                    <div class='now-ticket'>{latest.TicketNumber}</div>
                    <div class='now-counter'>Counter {latest.CounterNumber}</div>
                    <div class='now-dept'>{HtmlEncode(GetDeptName(latest.DepartmentId))}</div>";
                }
                else
                {
                    main = @"
                    <div class='now-ticket'>----</div>
                    <div class='now-counter'>Waiting</div>";
                }

                var hist = new StringBuilder();
                foreach (var call in recent)
                {
                    hist.Append($@"
                    <div class='history-item'>
                        <span class='history-ticket'>{call.TicketNumber}</span>
                        <span class='history-counter'>Counter {call.CounterNumber}</span>
                    </div>");
                }

                return $@"
                <div class='main-panel'>
                    <div class='now-label'>Now Serving</div>
                    {main}
                </div>
                <div class='sub-panel'>
                    <div class='history-label'>Recent</div>
                    {hist}
                </div>";
            }
        }

        // ============================================================
        // Queue Engine
        // ============================================================

        static int RegisterTicket(int departmentId)
        {
            int n;
            DateTime created;
            lock (Lock)
            {
                if (!NextTicketNumber.ContainsKey(departmentId)) return -1;
                NextTicketNumber[departmentId]++;
                n = NextTicketNumber[departmentId];
                created = DateTime.Now;
                WaitingQueues[departmentId].AddLast(n);
                Tickets[n] = new TicketInfo
                {
                    Number = n,
                    Dept = departmentId,
                    State = TicketState.Waiting,
                    CreatedAt = created
                };
            }
            try { TicketStore.InsertTicket(n, departmentId, created); }
            catch (Exception ex) { Console.WriteLine("[store] insert failed: " + ex.Message); }
            return n;
        }

        static int CallNextNumber(int counterNumber, int departmentId)
        {
            int ticket;
            lock (Lock)
            {
                if (!WaitingQueues.ContainsKey(departmentId)) return -1;
                var q = WaitingQueues[departmentId];
                if (q.Count == 0) return -1;
                ticket = q.First.Value;
                q.RemoveFirst();
                RecordCall(ticket, departmentId, counterNumber);
            }
            return ticket;
        }

        static int CallSpecificNumber(int counterNumber, int departmentId, int ticketNumber)
        {
            lock (Lock)
            {
                if (!WaitingQueues.ContainsKey(departmentId)) return -1;
                var q = WaitingQueues[departmentId];
                var node = q.Find(ticketNumber);
                if (node == null) return -1;
                q.Remove(node);
                RecordCall(ticketNumber, departmentId, counterNumber);
                return ticketNumber;
            }
        }

        // assumes lock is held
        static void RecordCall(int ticket, int dept, int counter)
        {
            var call = new QueueCall
            {
                TicketNumber = ticket,
                DepartmentId = dept,
                CounterNumber = counter,
                CalledAt = DateTime.Now
            };
            CallHistory.Add(call);
            LatestPerDept[dept] = call;
            if (Tickets.TryGetValue(ticket, out var info))
            {
                info.State = TicketState.Called;
                info.Counter = counter;
                info.CalledAt = call.CalledAt;
            }
            try { TicketStore.UpdateCalled(ticket, counter, call.CalledAt); }
            catch (Exception ex) { Console.WriteLine("[store] update failed: " + ex.Message); }
        }

        static void HydrateFromStore()
        {
            List<StoredTicket> rows;
            try { rows = TicketStore.LoadToday(); }
            catch (Exception ex) { Console.WriteLine("[store] load failed: " + ex.Message); return; }

            var calledOrdered = new List<StoredTicket>();
            foreach (var t in rows)
            {
                if (!WaitingQueues.ContainsKey(t.Dept)) continue;

                Tickets[t.Number] = new TicketInfo
                {
                    Number = t.Number,
                    Dept = t.Dept,
                    State = t.State == 1 ? TicketState.Called : TicketState.Waiting,
                    Counter = t.Counter,
                    CreatedAt = t.CreatedAt,
                    CalledAt = t.CalledAt
                };

                if (t.State == 0)
                    WaitingQueues[t.Dept].AddLast(t.Number);
                else
                    calledOrdered.Add(t);

                if (t.Number > NextTicketNumber[t.Dept])
                    NextTicketNumber[t.Dept] = t.Number;
            }

            calledOrdered.Sort((a, b) => a.CalledAt.CompareTo(b.CalledAt));
            foreach (var t in calledOrdered)
            {
                var call = new QueueCall
                {
                    TicketNumber = t.Number,
                    DepartmentId = t.Dept,
                    CounterNumber = t.Counter,
                    CalledAt = t.CalledAt
                };
                CallHistory.Add(call);
                LatestPerDept[t.Dept] = call;
            }

            Console.WriteLine($"[store] Hydrated {rows.Count} ticket(s) from today.");
        }

        // ============================================================
        // Pages
        // ============================================================

        static string PageRoot()
        {
            var deptButtons = new StringBuilder();
            foreach (var dept in Departments)
            {
                int waiting;
                lock (Lock) waiting = WaitingQueues[dept.Key].Count;
                deptButtons.Append($@"
<form method='post' action='/'>
  <input type='hidden' name='department-id' value='{dept.Key}'/>
  <button type='submit' class='dept-btn'>
    <span class='dept-name'>{HtmlEncode(dept.Value)}</span>
    <span class='dept-waiting'>{waiting} waiting</span>
  </button>
</form>");
            }

            return $@"<!DOCTYPE html>
<html><head><meta charset='utf-8'/><meta name='viewport' content='width=device-width, initial-scale=1'/><title>Take a Number</title>
<style>
*{{margin:0;padding:0;box-sizing:border-box;}}
body{{font-family:'Segoe UI',Arial,sans-serif;background:#f0f4f8;padding:20px;}}
h1{{text-align:center;color:#2d3748;margin-bottom:10px;font-size:28px;}}
.subtitle{{text-align:center;color:#718096;margin-bottom:30px;}}
.dept-btn{{display:block;width:100%;max-width:400px;margin:10px auto;padding:20px;font-size:18px;background:#4a90d9;color:#fff;border:none;border-radius:12px;cursor:pointer;text-align:left;}}
.dept-btn:hover{{background:#357abd;}}
.dept-name{{display:block;font-weight:bold;font-size:20px;}}
.dept-waiting{{display:block;font-size:14px;opacity:0.8;margin-top:4px;}}
form{{margin:0;}}
#root-body{{visibility:hidden;}}
</style></head>
<body>
<script>
(function(){{
  try{{
    var t=parseInt(localStorage.getItem('qt_ticket')||'0',10);
    var d=parseInt(localStorage.getItem('qt_dept')||'0',10);
    var date=localStorage.getItem('qt_date')||'';
    var today=new Date().toISOString().slice(0,10);
    if(date===today && t>0 && d>0){{
      location.replace('/m?t='+t+'&d='+d);
      return;
    }}
  }}catch(e){{}}
  document.addEventListener('DOMContentLoaded',function(){{
    document.getElementById('root-body').style.visibility='visible';
  }});
}})();
</script>
<div id='root-body'>
<h1>Queue Ticket</h1>
<p class='subtitle'>Select a service to take a number</p>
{deptButtons}
</div>
</body></html>";
        }

        static string PageDisplay()
        {
            return $@"<!DOCTYPE html>
<html><head>
<meta charset='utf-8'/><title>Queue Display</title>
<style>
* {{ margin:0; padding:0; box-sizing:border-box; }}
body {{ font-family:'Segoe UI',Arial,sans-serif; background:#1a1a2e; color:#fff; overflow:hidden; }}
.container {{ display:flex; width:100vw; height:100vh; }}
.main-panel {{ flex:1; display:flex; flex-direction:column; align-items:center; justify-content:center; background:#16213e; border-right:3px solid #0f3460; }}
.now-label {{ font-size:3vw; color:#a8b2d1; text-transform:uppercase; letter-spacing:0.3em; margin-bottom:1vh; }}
.now-ticket {{ font-size:12vw; font-weight:bold; color:#e94560; line-height:1; }}
.now-counter {{ font-size:3vw; color:#a8b2d1; margin-top:1vh; }}
.now-dept {{ font-size:2vw; color:#6c7a96; margin-top:1vh; }}
.sub-panel {{ width:35%; display:flex; flex-direction:column; padding:3vh 2vw; background:#1a1a2e; }}
.history-label {{ font-size:2vw; color:#a8b2d1; text-transform:uppercase; letter-spacing:0.2em; margin-bottom:2vh; padding-bottom:1vh; border-bottom:2px solid #0f3460; }}
.history-item {{ display:flex; justify-content:space-between; align-items:center; padding:2vh 1vw; margin-bottom:1vh; background:#16213e; border-radius:8px; border-left:4px solid #0f3460; }}
.history-ticket {{ font-size:3vw; font-weight:bold; color:#e2e8f0; }}
.history-counter {{ font-size:1.8vw; color:#a8b2d1; }}
.status {{ position:fixed; bottom:1vh; right:1vw; font-size:1.2vw; color:#4a5568; }}
.status.connected {{ color:#48bb78; }}
.status.disconnected {{ color:#e94560; }}
</style></head>
<body>
<div id='container' class='container'>
<div class='main-panel'><div class='now-label'>Now Serving</div><div class='now-ticket'>----</div><div class='now-counter'>Waiting</div></div>
<div class='sub-panel'><div class='history-label'>Recent</div></div>
</div>
<div id='status' class='status'>Connecting...</div>
<script>
function connectSSE(){{
  var s=new EventSource('/sse-display');
  var st=document.getElementById('status');
  s.addEventListener('display',function(e){{ document.getElementById('container').innerHTML=e.data; }});
  s.onopen=function(){{ st.textContent='Connected'; st.className='status connected'; }};
  s.onerror=function(){{ st.textContent='Reconnecting...'; st.className='status disconnected'; }};
}}
connectSSE();
</script></body></html>";
        }

        static string PageTerminal(HttpRequestMessage request)
        {
            string message = "";

            if (request.Method == "POST")
            {
                string action = request.Form["action"] ?? "";
                if (action == "take-number")
                {
                    int deptId = TryInt(request.Form["department-id"]);
                    if (Departments.ContainsKey(deptId))
                    {
                        int ticket = RegisterTicket(deptId);
                        Printer.PrintTicket(Config.PrinterName, 0, ticket, Departments[deptId]);
                        message = $@"
<div class='ticket-result'>
<div class='ticket-label'>Your Number</div>
<div class='ticket-number'>{ticket}</div>
<div class='ticket-dept'>{HtmlEncode(Departments[deptId])}</div>
<div class='ticket-msg'>Please wait for your number to be called.</div>
<div class='ticket-msg'>Mobile: <code>/m?t={ticket}&amp;d={deptId}</code></div>
</div>";
                    }
                }
            }

            var deptButtons = new StringBuilder();
            foreach (var dept in Departments)
            {
                int waiting;
                lock (Lock) waiting = WaitingQueues[dept.Key].Count;
                deptButtons.Append($@"
<form method='post' action='/terminal'>
  <input type='hidden' name='action' value='take-number'/>
  <input type='hidden' name='department-id' value='{dept.Key}'/>
  <button type='submit' class='dept-btn'>
    <span class='dept-name'>{HtmlEncode(dept.Value)}</span>
    <span class='dept-waiting'>{waiting} waiting</span>
  </button>
</form>");
            }

            return $@"<!DOCTYPE html>
<html><head><meta charset='utf-8'/><meta name='viewport' content='width=device-width, initial-scale=1'/><title>Take a Number</title>
<style>
*{{margin:0;padding:0;box-sizing:border-box;}}
body{{font-family:'Segoe UI',Arial,sans-serif;background:#f0f4f8;padding:20px;}}
h1{{text-align:center;color:#2d3748;margin-bottom:10px;font-size:28px;}}
.subtitle{{text-align:center;color:#718096;margin-bottom:30px;}}
.dept-btn{{display:block;width:100%;max-width:400px;margin:10px auto;padding:20px;font-size:18px;background:#4a90d9;color:#fff;border:none;border-radius:12px;cursor:pointer;text-align:left;}}
.dept-btn:hover{{background:#357abd;}}
.dept-name{{display:block;font-weight:bold;font-size:20px;}}
.dept-waiting{{display:block;font-size:14px;opacity:0.8;margin-top:4px;}}
form{{margin:0;}}
.ticket-result{{max-width:400px;margin:0 auto 30px auto;padding:30px;background:#fff;border-radius:16px;text-align:center;box-shadow:0 4px 12px rgba(0,0,0,0.1);border:2px dashed #4a90d9;}}
.ticket-label{{font-size:16px;color:#718096;text-transform:uppercase;letter-spacing:0.1em;}}
.ticket-number{{font-size:64px;font-weight:bold;color:#e94560;margin:10px 0;}}
.ticket-dept{{font-size:18px;color:#4a5568;}}
.ticket-msg{{font-size:14px;color:#a0aec0;margin-top:15px;}}
.home-link{{display:block;text-align:center;margin-top:30px;color:#4a90d9;}}
</style></head>
<body>
<h1>Queue Ticket</h1>
<p class='subtitle'>Select a service to take a number</p>
{message}
{deptButtons}
<a class='home-link' href='/display' target='_blank'>View Display Board</a>
</body></html>";
        }

        static string PageOperator(HttpRequestMessage request)
        {
            string message = "";
            string counterNumber = request.Form["counter-number"] ?? "";
            string departmentId = request.Form["department-id"] ?? "";
            bool isSetup = string.IsNullOrEmpty(counterNumber) || counterNumber == "0";

            if (request.Method == "POST")
            {
                string action = request.Form["action"] ?? "";
                if (action == "call-next")
                {
                    int counter = TryInt(request.Form["counter-number"]);
                    int deptId = TryInt(request.Form["department-id"]);
                    int ticket = CallNextNumber(counter, deptId);
                    if (ticket > 0)
                    {
                        Console.WriteLine($"  [Audio] Now serving #{ticket} at Counter {counter}");
                        BroadcastDisplay();
                        BroadcastMobile(deptId);
                        message = $"<div class='msg success'>Called #{ticket} to Counter {counter}</div>";
                    }
                    else
                    {
                        message = "<div class='msg empty'>No tickets waiting in queue.</div>";
                    }
                    counterNumber = counter.ToString();
                    departmentId = deptId.ToString();
                    isSetup = false;
                }
                else if (action == "call-specific")
                {
                    int counter = TryInt(request.Form["counter-number"]);
                    int deptId = TryInt(request.Form["department-id"]);
                    int specific = TryInt(request.Form["ticket-number"]);
                    int ticket = CallSpecificNumber(counter, deptId, specific);
                    if (ticket > 0)
                    {
                        Console.WriteLine($"  [Audio] Now serving #{ticket} at Counter {counter}");
                        BroadcastDisplay();
                        BroadcastMobile(deptId);
                        message = $"<div class='msg success'>Called #{ticket} to Counter {counter}</div>";
                    }
                    else
                    {
                        message = $"<div class='msg empty'>Ticket #{specific} not found in queue.</div>";
                    }
                    counterNumber = counter.ToString();
                    departmentId = deptId.ToString();
                    isSetup = false;
                }
                else if (action == "setup")
                {
                    isSetup = false;
                }
            }

            if (isSetup) return PageOperatorSetup();

            int deptIdInt = TryInt(departmentId);
            string deptName = GetDeptName(deptIdInt);
            int waitingCount; string waitingList;
            lock (Lock)
            {
                var q = WaitingQueues.ContainsKey(deptIdInt) ? WaitingQueues[deptIdInt] : new LinkedList<int>();
                waitingCount = q.Count;
                waitingList = string.Join(", ", q);
            }

            return $@"<!DOCTYPE html>
<html><head><meta charset='utf-8'/><meta name='viewport' content='width=device-width, initial-scale=1'/>
<title>Operator — Counter {HtmlEncode(counterNumber)}</title>
<style>
*{{margin:0;padding:0;box-sizing:border-box;}}
body{{font-family:'Segoe UI',Arial,sans-serif;background:#f0f4f8;padding:20px;max-width:500px;margin:0 auto;}}
h1{{color:#2d3748;margin-bottom:5px;}}
.counter-info{{color:#718096;margin-bottom:20px;font-size:14px;}}
.msg{{padding:15px;border-radius:8px;margin-bottom:20px;font-size:16px;font-weight:bold;}}
.msg.success{{background:#c6f6d5;color:#22543d;}}
.msg.empty{{background:#fed7d7;color:#742a2a;}}
.call-btn{{display:block;width:100%;padding:20px;font-size:20px;font-weight:bold;background:#48bb78;color:#fff;border:none;border-radius:12px;cursor:pointer;margin-bottom:15px;}}
.call-btn:hover{{background:#38a169;}}
.specific-section{{background:#fff;padding:20px;border-radius:12px;margin-bottom:20px;box-shadow:0 2px 8px rgba(0,0,0,0.06);}}
.specific-section input[type=number]{{width:100%;padding:12px;font-size:18px;border:2px solid #e2e8f0;border-radius:8px;margin:10px 0;}}
.specific-btn{{display:block;width:100%;padding:14px;font-size:16px;background:#4a90d9;color:#fff;border:none;border-radius:8px;cursor:pointer;}}
.waiting-info{{background:#fff;padding:15px;border-radius:12px;margin-bottom:20px;box-shadow:0 2px 8px rgba(0,0,0,0.06);}}
.waiting-list{{color:#718096;font-size:14px;margin-top:5px;word-break:break-all;}}
.reset-link{{display:block;text-align:center;margin-top:20px;color:#a0aec0;font-size:14px;}}
</style></head><body>
<h1>Counter {HtmlEncode(counterNumber)}</h1>
<div class='counter-info'>{HtmlEncode(deptName)}</div>
{message}
<div class='waiting-info'><strong>Waiting: {waitingCount}</strong>
<div class='waiting-list'>{(waitingCount > 0 ? waitingList : "—")}</div></div>
<form method='post' action='/operator'>
<input type='hidden' name='action' value='call-next'/>
<input type='hidden' name='counter-number' value='{HtmlEncode(counterNumber)}'/>
<input type='hidden' name='department-id' value='{HtmlEncode(departmentId)}'/>
<button type='submit' class='call-btn'>Call Next Number</button>
</form>
<div class='specific-section'>
<form method='post' action='/operator'>
<input type='hidden' name='action' value='call-specific'/>
<input type='hidden' name='counter-number' value='{HtmlEncode(counterNumber)}'/>
<input type='hidden' name='department-id' value='{HtmlEncode(departmentId)}'/>
<label>Call Specific Number:</label>
<input type='number' name='ticket-number' placeholder='Enter ticket number'/>
<button type='submit' class='specific-btn'>Call This Number</button>
</form></div>
<a class='reset-link' href='/operator'>Change Counter</a>
</body></html>";
        }

        static string PageOperatorSetup()
        {
            var opts = new StringBuilder();
            foreach (var dept in Departments)
                opts.Append($"<option value='{dept.Key}'>{HtmlEncode(dept.Value)}</option>");

            return $@"<!DOCTYPE html>
<html><head><meta charset='utf-8'/><meta name='viewport' content='width=device-width, initial-scale=1'/><title>Operator Setup</title>
<style>
*{{margin:0;padding:0;box-sizing:border-box;}}
body{{font-family:'Segoe UI',Arial,sans-serif;background:#f0f4f8;display:flex;align-items:center;justify-content:center;min-height:100vh;}}
.setup-box{{background:#fff;padding:40px;border-radius:16px;box-shadow:0 4px 12px rgba(0,0,0,0.1);width:100%;max-width:400px;}}
h1{{color:#2d3748;margin-bottom:25px;text-align:center;}}
label{{display:block;font-weight:bold;color:#4a5568;margin-bottom:6px;margin-top:15px;}}
input[type=number],select{{width:100%;padding:12px;font-size:18px;border:2px solid #e2e8f0;border-radius:8px;}}
.setup-btn{{display:block;width:100%;padding:16px;font-size:18px;font-weight:bold;background:#4a90d9;color:#fff;border:none;border-radius:12px;cursor:pointer;margin-top:25px;}}
</style></head><body>
<div class='setup-box'><h1>Operator Setup</h1>
<form method='post' action='/operator'>
<input type='hidden' name='action' value='setup'/>
<label>Counter Number:</label>
<input type='number' name='counter-number' min='1' max='99' value='1' required/>
<label>Department:</label>
<select name='department-id'>{opts}</select>
<button type='submit' class='setup-btn'>Start</button>
</form></div></body></html>";
        }

        // --- Mobile Page (customer's phone) ---
        // Hybrid: polls /status every 5s, switches to /sse-mobile when position <= 3,
        // switches back to polling when position > 5 (hysteresis).
        static string PageMobile(HttpRequestMessage request)
        {
            int t = TryInt(request.QueryString["t"]);
            int d = TryInt(request.QueryString["d"]);
            string deptName = GetDeptName(d);

            return $@"<!DOCTYPE html>
<html><head><meta charset='utf-8'/><meta name='viewport' content='width=device-width, initial-scale=1'/>
<title>My Ticket</title>
<style>
*{{margin:0;padding:0;box-sizing:border-box;}}
body{{font-family:'Segoe UI',Arial,sans-serif;background:#1a1a2e;color:#fff;min-height:100vh;padding:20px;}}
.card{{max-width:400px;margin:0 auto;background:#16213e;border-radius:16px;padding:30px 20px;text-align:center;}}
.label{{font-size:14px;color:#a8b2d1;text-transform:uppercase;letter-spacing:0.2em;}}
.dept{{font-size:18px;color:#a8b2d1;margin:10px 0 20px;}}
.my-ticket{{font-size:72px;font-weight:bold;color:#e94560;line-height:1;margin:10px 0;}}
.position{{font-size:20px;color:#e2e8f0;margin:20px 0;}}
.position .num{{font-size:48px;font-weight:bold;color:#48bb78;display:block;}}
.called{{background:#48bb78;color:#fff;padding:30px 20px;border-radius:12px;margin-top:20px;display:none;}}
.called.show{{display:block;}}
.called .big{{font-size:36px;font-weight:bold;}}
.called .ctr{{font-size:64px;font-weight:bold;margin-top:10px;}}
.serving{{margin-top:30px;background:#0f3460;padding:20px;border-radius:12px;}}
.serving .label{{margin-bottom:10px;}}
.serving .num{{font-size:36px;font-weight:bold;color:#e94560;}}
.upcoming{{margin-top:20px;}}
.upcoming .row{{display:flex;justify-content:space-between;padding:8px 12px;background:#0f3460;border-radius:8px;margin-bottom:6px;font-size:16px;}}
.mode{{margin-top:30px;font-size:11px;color:#4a5568;}}
.mode .live{{color:#48bb78;}}
.new-btn{{display:block;width:100%;max-width:400px;margin:30px auto 0;padding:14px;font-size:16px;background:#0f3460;color:#fff;border:1px solid #2d3a5e;border-radius:10px;cursor:pointer;}}
.new-btn:hover{{background:#16213e;}}
.confirm-box{{display:none;max-width:400px;margin:15px auto 0;padding:20px;background:#16213e;border:1px solid #2d3a5e;border-radius:12px;text-align:center;}}
.confirm-msg{{font-size:15px;color:#e2e8f0;margin-bottom:15px;}}
.confirm-actions{{display:flex;gap:10px;justify-content:center;}}
.btn-yes,.btn-no{{flex:1;padding:12px;font-size:16px;font-weight:bold;border:none;border-radius:8px;cursor:pointer;}}
.btn-yes{{background:#e94560;color:#fff;}}
.btn-no{{background:#48bb78;color:#fff;}}
</style></head><body>
<div class='card'>
<div class='label'>Your Ticket</div>
<div class='dept'>{HtmlEncode(deptName)}</div>
<div class='my-ticket'>{t}</div>

<div id='waiting-section'>
<div class='position'>People ahead: <span id='position' class='num'>—</span></div>
</div>

<div id='called' class='called'>
<div class='big'>YOUR TURN</div>
<div>Please proceed to</div>
<div class='ctr'>Counter <span id='counter'>—</span></div>
</div>

<div class='serving'>
<div class='label'>Now Serving (this dept)</div>
<div class='num' id='latest'>—</div>
</div>

<div class='upcoming'>
<div class='label' style='text-align:left;margin-bottom:8px;'>Next in queue</div>
<div id='upcoming'></div>
</div>

<div class='mode'>Mode: <span id='mode'>polling</span></div>

<button id='new-btn' class='new-btn'>Request new number</button>
<div id='confirm-box' class='confirm-box'>
  <div class='confirm-msg'>Are you sure to discard this and get a new ticket?</div>
  <div class='confirm-actions'>
    <button id='confirm-yes' class='btn-yes'>Yes</button>
    <button id='confirm-no' class='btn-no'>No</button>
  </div>
</div>
</div>

<script>
var TICKET={t}, DEPT={d};
try{{
  if(TICKET>0 && DEPT>0){{
    localStorage.setItem('qt_ticket', String(TICKET));
    localStorage.setItem('qt_dept', String(DEPT));
    localStorage.setItem('qt_date', new Date().toISOString().slice(0,10));
  }}
}}catch(e){{}}

document.getElementById('new-btn').addEventListener('click',function(){{
  document.getElementById('confirm-box').style.display='block';
}});
document.getElementById('confirm-no').addEventListener('click',function(){{
  document.getElementById('confirm-box').style.display='none';
}});
document.getElementById('confirm-yes').addEventListener('click',function(){{
  try{{
    localStorage.removeItem('qt_ticket');
    localStorage.removeItem('qt_dept');
    localStorage.removeItem('qt_date');
  }}catch(e){{}}
  location.replace('/');
}});
</script>
<script>
const POLL_MS=5000;
const SSE_THRESHOLD=3;
const POLL_THRESHOLD=5;
let mode='poll';
let pollTimer=null;
let sse=null;

function render(s){{
  document.getElementById('latest').textContent = s.latest>0 ? s.latest : '—';
  let up=document.getElementById('upcoming'); up.innerHTML='';
  (s.upcoming||[]).slice(0,4).forEach(n=>{{
    let row=document.createElement('div'); row.className='row';
    let a=document.createElement('span'); a.textContent='#'+n;
    row.appendChild(a); up.appendChild(row);
  }});
  if(s.status===1){{
    document.getElementById('waiting-section').style.display='none';
    document.getElementById('called').classList.add('show');
    document.getElementById('counter').textContent=s.counter;
  }} else {{
    document.getElementById('waiting-section').style.display='block';
    document.getElementById('called').classList.remove('show');
    document.getElementById('position').textContent = s.position<0 ? '?' : s.position;
    decideMode(s.position);
  }}
}}

function decideMode(pos){{
  if(mode==='poll' && pos>=0 && pos<=SSE_THRESHOLD) switchToSse();
  else if(mode==='sse' && pos>POLL_THRESHOLD) switchToPoll();
}}

async function poll(){{
  try{{
    let r=await fetch('/status?t='+TICKET+'&d='+DEPT,{{cache:'no-store'}});
    let s=await r.json(); render(s);
  }}catch(e){{}}
}}

function switchToSse(){{
  mode='sse';
  document.getElementById('mode').innerHTML='<span class=live>live (SSE)</span>';
  if(pollTimer){{ clearInterval(pollTimer); pollTimer=null; }}
  sse=new EventSource('/sse-mobile?t='+TICKET+'&d='+DEPT);
  sse.addEventListener('status',e=>{{ try{{ render(JSON.parse(e.data)); }}catch(_){{ }} }});
  sse.onerror=()=>{{ try{{sse.close();}}catch(_){{ }} sse=null; switchToPoll(); }};
}}

function switchToPoll(){{
  mode='poll';
  document.getElementById('mode').textContent='polling (5s)';
  if(sse){{ try{{sse.close();}}catch(_){{ }} sse=null; }}
  poll();
  pollTimer=setInterval(poll, POLL_MS);
}}

switchToPoll();
</script></body></html>";
        }

        static string PageNotFound()
        {
            return @"<!DOCTYPE html><html><head><meta charset='utf-8'/><title>Not Found</title>
<style>body{font-family:Arial,sans-serif;text-align:center;padding:60px;background:#f0f4f8;} h1{color:#e94560;} a{color:#4a90d9;}</style></head>
<body><h1>404 — Not Found</h1>
<p><a href='/display'>Display</a> | <a href='/terminal'>Terminal</a> | <a href='/operator'>Operator</a></p></body></html>";
        }

        // ============================================================
        // SQLite roundtrip test — synchronous: create DB, table, insert, select
        // ============================================================

        static string RunSqliteRoundtrip()
        {
            string dbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "sqlite-test.db");
            var sw = Stopwatch.StartNew();
            var steps = new List<string>();
            string error = null;
            int readId = 0;
            string readName = null;
            long readTs = 0;

            try
            {
                if (File.Exists(dbPath)) File.Delete(dbPath);
                steps.Add("deleted-existing");

                SQLiteConnection.CreateFile(dbPath);
                steps.Add("created-database");

                string connStr = "Data Source=" + dbPath + ";Version=3;";
                using (var conn = new SQLiteConnection(connStr))
                {
                    conn.Open();
                    steps.Add("opened-connection");

                    using (var cmd = new SQLiteCommand(
                        "CREATE TABLE rt (id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT NOT NULL, ts INTEGER NOT NULL);", conn))
                    {
                        cmd.ExecuteNonQuery();
                    }
                    steps.Add("created-table");

                    long tsNow = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                    using (var cmd = new SQLiteCommand("INSERT INTO rt (name, ts) VALUES (@n, @t);", conn))
                    {
                        cmd.Parameters.AddWithValue("@n", "hello-sqlite");
                        cmd.Parameters.AddWithValue("@t", tsNow);
                        cmd.ExecuteNonQuery();
                    }
                    steps.Add("inserted-row");

                    using (var cmd = new SQLiteCommand("SELECT id, name, ts FROM rt LIMIT 1;", conn))
                    using (var rdr = cmd.ExecuteReader())
                    {
                        if (rdr.Read())
                        {
                            readId = Convert.ToInt32(rdr["id"]);
                            readName = rdr["name"].ToString();
                            readTs = Convert.ToInt64(rdr["ts"]);
                        }
                    }
                    steps.Add("selected-row");
                }
            }
            catch (Exception ex)
            {
                error = ex.Message;
            }

            sw.Stop();

            var sb = new StringBuilder();
            sb.Append("{");
            sb.Append("\"ok\":").Append(error == null ? "true" : "false").Append(",");
            sb.Append("\"elapsedMs\":").Append(sw.ElapsedMilliseconds).Append(",");
            sb.Append("\"dbPath\":\"").Append(JsonEscape(dbPath)).Append("\",");
            sb.Append("\"steps\":[");
            for (int i = 0; i < steps.Count; i++)
            {
                if (i > 0) sb.Append(",");
                sb.Append("\"").Append(JsonEscape(steps[i])).Append("\"");
            }
            sb.Append("],");
            sb.Append("\"row\":{");
            sb.Append("\"id\":").Append(readId).Append(",");
            sb.Append("\"name\":\"").Append(JsonEscape(readName ?? "")).Append("\",");
            sb.Append("\"ts\":").Append(readTs);
            sb.Append("}");
            if (error != null)
                sb.Append(",\"error\":\"").Append(JsonEscape(error)).Append("\"");
            sb.Append("}");
            return sb.ToString();
        }

        static string JsonEscape(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(s.Length);
            foreach (char c in s)
            {
                switch (c)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '"': sb.Append("\\\""); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.AppendFormat("\\u{0:x4}", (int)c);
                        else sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }

        // ============================================================
        // Utility
        // ============================================================

        static string GetDeptName(int deptId) =>
            Departments.ContainsKey(deptId) ? Departments[deptId] : "Unknown";

        static int TryInt(string s)
        {
            int n; int.TryParse(s ?? "", out n); return n;
        }

        static string HtmlEncode(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            return text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
                       .Replace("\"", "&quot;").Replace("'", "&#39;");
        }
    }

    // ============================================================
    // Models
    // ============================================================

    class QueueCall
    {
        public int TicketNumber { get; set; }
        public int DepartmentId { get; set; }
        public int CounterNumber { get; set; }
        public DateTime CalledAt { get; set; }
    }

    enum TicketState { Waiting, Called }

    class TicketInfo
    {
        public int Number { get; set; }
        public int Dept { get; set; }
        public TicketState State { get; set; }
        public int Counter { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime CalledAt { get; set; }
    }

    class SseDisplayClient
    {
        public TcpClient Client { get; set; }
        public NetworkStream Stream { get; set; }
    }

    class SseMobileClient
    {
        public TcpClient Client { get; set; }
        public NetworkStream Stream { get; set; }
        public int Ticket { get; set; }
        public int Dept { get; set; }
    }
}