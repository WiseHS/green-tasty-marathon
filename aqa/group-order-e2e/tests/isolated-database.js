import pg from 'pg';

// Fault injection is deliberately unavailable against Render/Neon or other remote hosts.
// CI provisions this disposable database; no application test backdoor is exposed.
export class IsolatedDatabase {
  constructor() { this.originals = new Map(); }
  async connect() {
    if (process.env.AQA_ISOLATED_DATABASE !== 'true') throw new Error('Fault injection requires the isolated CI database.');
    const database = new URL(process.env.AQA_DATABASE_URL || '');
    const api = new URL(process.env.API_URL || '');
    if (![database.hostname, api.hostname].every(host => ['localhost', '127.0.0.1', '[::1]'].includes(host)) ||
        database.pathname !== '/restaurant') throw new Error('Only the loopback CI restaurant database is allowed.');
    this.client = new pg.Client({connectionString: database.href});
    await this.client.connect();
  }
  async change(kind, id, patch) {
    if (!['Reservation', 'GroupReservation', 'GuestSession', 'Dish', 'Order'].includes(kind)) throw new Error('Unsupported fixture kind');
    if (!this.client) await this.connect();
    const result = await this.client.query("SELECT id,payload FROM app_documents WHERE kind=$1 AND (id=$2 OR ($1='Order' AND payload->>'orderId'=$2))", [kind, id]);
    if (result.rowCount !== 1) throw new Error(`Fixture ${kind} was not found`);
    id = result.rows[0].id; // Orders have a composite persistence key; API IDs are not storage keys.
    const key = `${kind}:${id}`;
    if (!this.originals.has(key)) this.originals.set(key, {kind, id, payload: result.rows[0].payload});
    if (patch === null) await this.client.query('DELETE FROM app_documents WHERE kind=$1 AND id=$2', [kind, id]);
    else await this.client.query('UPDATE app_documents SET payload=$3::jsonb WHERE kind=$1 AND id=$2',
      [kind, id, JSON.stringify({...result.rows[0].payload, ...patch})]);
  }
  async restore() {
    if (!this.client) return;
    try {
      for (const {kind, id, payload} of [...this.originals.values()].reverse())
        await this.client.query('INSERT INTO app_documents(kind,id,payload) VALUES($1,$2,$3::jsonb) ON CONFLICT(kind,id) DO UPDATE SET payload=excluded.payload',
          [kind, id, JSON.stringify(payload)]);
    } finally { await this.client.end(); }
  }
}
