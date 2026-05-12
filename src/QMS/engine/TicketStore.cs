using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;

namespace System.Engine
{
    public class StoredTicket
    {
        public int Number;
        public int Dept;
        public int State;        // 0 waiting, 1 called
        public int Counter;
        public DateTime CreatedAt;
        public DateTime CalledAt;
    }

    public static class TicketStore
    {
        private static SQLiteConnection _conn;
        private static readonly object _writeLock = new object();

        public static void Init(string dbPath)
        {
            bool fresh = !File.Exists(dbPath);
            if (fresh) SQLiteConnection.CreateFile(dbPath);

            _conn = new SQLiteConnection("Data Source=" + dbPath + ";Version=3;");
            _conn.Open();

            Exec("PRAGMA journal_mode=WAL;");
            Exec("PRAGMA synchronous=FULL;");

            Exec(@"
                CREATE TABLE IF NOT EXISTS tickets (
                    number      INTEGER PRIMARY KEY,
                    dept        INTEGER NOT NULL,
                    state       INTEGER NOT NULL,
                    counter     INTEGER NOT NULL DEFAULT 0,
                    created_at  TEXT NOT NULL,
                    called_at   TEXT
                );");
            Exec("CREATE INDEX IF NOT EXISTS ix_tickets_dept_state ON tickets(dept, state);");
            Exec("CREATE INDEX IF NOT EXISTS ix_tickets_created    ON tickets(created_at);");

            Console.WriteLine("[store] " + (fresh ? "Created" : "Opened") + " " + dbPath);
        }

        // Returns today's tickets ordered by ticket number ascending.
        public static List<StoredTicket> LoadToday()
        {
            var list = new List<StoredTicket>();
            string today = DateTime.Now.ToString("yyyy-MM-dd");
            using (var cmd = new SQLiteCommand(
                "SELECT number, dept, state, counter, created_at, called_at " +
                "FROM tickets WHERE substr(created_at,1,10)=@d ORDER BY number ASC;", _conn))
            {
                cmd.Parameters.AddWithValue("@d", today);
                using (var rdr = cmd.ExecuteReader())
                {
                    while (rdr.Read())
                    {
                        var t = new StoredTicket
                        {
                            Number = Convert.ToInt32(rdr["number"]),
                            Dept = Convert.ToInt32(rdr["dept"]),
                            State = Convert.ToInt32(rdr["state"]),
                            Counter = Convert.ToInt32(rdr["counter"]),
                            CreatedAt = DateTime.Parse(rdr["created_at"].ToString())
                        };
                        var ca = rdr["called_at"];
                        if (ca != DBNull.Value && ca != null && ca.ToString().Length > 0)
                            t.CalledAt = DateTime.Parse(ca.ToString());
                        list.Add(t);
                    }
                }
            }
            return list;
        }

        public static void InsertTicket(int number, int dept, DateTime createdAt)
        {
            lock (_writeLock)
            {
                using (var cmd = new SQLiteCommand(
                    "INSERT INTO tickets (number, dept, state, counter, created_at) VALUES (@n,@d,0,0,@c);", _conn))
                {
                    cmd.Parameters.AddWithValue("@n", number);
                    cmd.Parameters.AddWithValue("@d", dept);
                    cmd.Parameters.AddWithValue("@c", createdAt.ToString("yyyy-MM-dd HH:mm:ss"));
                    cmd.ExecuteNonQuery();
                }
            }
        }

        public static void UpdateCalled(int number, int counter, DateTime calledAt)
        {
            lock (_writeLock)
            {
                using (var cmd = new SQLiteCommand(
                    "UPDATE tickets SET state=1, counter=@k, called_at=@t WHERE number=@n;", _conn))
                {
                    cmd.Parameters.AddWithValue("@k", counter);
                    cmd.Parameters.AddWithValue("@t", calledAt.ToString("yyyy-MM-dd HH:mm:ss"));
                    cmd.Parameters.AddWithValue("@n", number);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        private static void Exec(string sql)
        {
            using (var cmd = new SQLiteCommand(sql, _conn))
                cmd.ExecuteNonQuery();
        }
    }
}
