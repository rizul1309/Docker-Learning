import { useState, useEffect, useCallback } from 'react'

const API = '/api/v1'

export default function App() {
  const [triggers, setTriggers] = useState([])
  const [reports, setReports] = useState([])
  const [stats, setStats] = useState(null)
  const [cacheKeys, setCacheKeys] = useState(null)
  const [selectedTrigger, setSelectedTrigger] = useState(null)
  const [executions, setExecutions] = useState([])
  const [logs, setLogs] = useState([])
  const [health, setHealth] = useState(null)

  const addLog = useCallback((msg) => {
    setLogs(prev => [`[${new Date().toLocaleTimeString()}] ${msg}`, ...prev].slice(0, 50))
  }, [])

  const fetchTriggers = useCallback(async () => {
    console.info(`[Triggers] Starting fetchTrigger function from ${API}/triggers`)
    const res = await fetch(`${API}/triggers`)
    if (res.ok) {
      const data = await res.json()
      setTriggers(data)
      addLog(`Fetched ${data.length} triggers`)
    }
  }, [addLog])

  const fetchReports = useCallback(async () => {
    const res = await fetch(`${API}/reports`)
    if (res.ok) setReports(await res.json())
  }, [])

  const fetchStats = useCallback(async () => {
    const res = await fetch(`${API}/jobs/statistics`)
    if (res.ok) {
      const data = await res.json()
      setStats(data)
    }
  }, [])

  const fetchCacheKeys = useCallback(async () => {
    const res = await fetch(`${API}/cache/keys`)
    if (res.ok) setCacheKeys(await res.json())
  }, [])

  const fetchHealth = useCallback(async () => {
    try {
      const res = await fetch('/health')
      setHealth(res.ok ? 'Healthy' : 'Unhealthy')
    } catch { setHealth('Unreachable') }
  }, [])

  const fetchExecutions = useCallback(async (triggerId) => {
    const res = await fetch(`${API}/triggers/${triggerId}/executions`)
    if (res.ok) setExecutions(await res.json())
  }, [])

  useEffect(() => {
    fetchTriggers()
    fetchReports()
    fetchStats()
    fetchHealth()
    const interval = setInterval(() => { fetchStats(); fetchHealth() }, 5000)
    return () => clearInterval(interval)
  }, [fetchTriggers, fetchReports, fetchStats, fetchHealth])

  const runTrigger = async (triggerId, triggerName) => {
    addLog(`Running trigger: ${triggerName}...`)
    const res = await fetch(`${API}/triggers/${triggerId}/run`, { method: 'POST' })
    if (res.ok) {
      const data = await res.json()
      addLog(`✓ Execution created: ${data.executionId} (Job #${data.jobId})`)
      fetchStats()
      if (selectedTrigger === triggerId) fetchExecutions(triggerId)
    } else {
      addLog(`✗ Failed to run trigger`)
    }
  }

  const dequeueJob = async () => {
    addLog('Dequeuing next job...')
    const res = await fetch(`${API}/jobs/next-job`)
    if (res.status === 204) {
      addLog('No jobs in queue')
      return
    }
    if (res.ok) {
      const job = await res.json()
      addLog(`✓ Dequeued Job #${job.jobId}: ${job.reportDisplayName} (${job.outputFormat})`)
      fetchStats()

      // Simulate processing and complete the job
      addLog(`Processing Job #${job.jobId}...`)
      setTimeout(async () => {
        const completeRes = await fetch(`${API}/jobs/${job.jobId}/complete`, {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({
            success: true,
            executionTimeSeconds: 3,
            resultPath: `results/${job.executionId}.csv`,
            logPath: `logs/${job.executionId}.log`
          })
        })
        if (completeRes.ok) {
          addLog(`✓ Job #${job.jobId} completed successfully`)
          fetchStats()
          if (selectedTrigger) fetchExecutions(selectedTrigger)
        }
      }, 2000)
    }
  }

  const flushCache = async () => {
    await fetch(`${API}/cache/flush`, { method: 'DELETE' })
    addLog('Redis cache flushed')
    setCacheKeys(null)
    fetchCacheKeys()
  }

  const selectTrigger = (triggerId) => {
    setSelectedTrigger(triggerId)
    fetchExecutions(triggerId)
  }

  return (
    <div style={{ fontFamily: 'system-ui, sans-serif', maxWidth: 1200, margin: '0 auto', padding: 20, background: '#0d1117', color: '#c9d1d9', minHeight: '100vh' }}>
      <h1 style={{ color: '#58a6ff', borderBottom: '1px solid #30363d', paddingBottom: 10 }}>
        Batch Processing Dashboard
      </h1>

      {/* Health + Stats Bar */}
      <div style={{ display: 'flex', gap: 20, marginBottom: 20 }}>
        <Card title="System Health" style={{ flex: 1 }}>
          <StatusBadge status={health} />
        </Card>
        <Card title="Queue Statistics" style={{ flex: 2 }}>
          {stats ? (
            <div style={{ display: 'flex', gap: 30 }}>
              <Stat label="Total Queued" value={stats.totalQueued} />
              <Stat label="Executing" value={stats.currentlyExecuting} />
              <Stat label="Waiting" value={stats.waitingForRunner} />
            </div>
          ) : <span>Loading...</span>}
        </Card>
        <Card title="Actions" style={{ flex: 1 }}>
          <button onClick={dequeueJob} style={btnStyle('#238636')}>
            Dequeue & Process Job
          </button>
        </Card>
      </div>

      <div style={{ display: 'flex', gap: 20 }}>
        {/* Left: Triggers */}
        <div style={{ flex: 1 }}>
          <Card title={`Trigger Definitions (${triggers.length})`}>
            <button onClick={fetchTriggers} style={btnStyle('#30363d', { marginBottom: 10, fontSize: 12 })}>
              Refresh
            </button>
            {triggers.map(t => (
              <div key={t.dataTriggerId}
                onClick={() => selectTrigger(t.dataTriggerId)}
                style={{
                  padding: 10, marginBottom: 8, borderRadius: 6, cursor: 'pointer',
                  background: selectedTrigger === t.dataTriggerId ? '#1f2937' : '#161b22',
                  border: `1px solid ${selectedTrigger === t.dataTriggerId ? '#58a6ff' : '#30363d'}`
                }}>
                <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                  <div>
                    <strong style={{ color: '#f0f6fc' }}>{t.triggerName}</strong>
                    <div style={{ fontSize: 12, color: '#8b949e', marginTop: 2 }}>
                      {t.reportName} · {t.outputFormat} · {t.frequency}
                      {t.emailEnabled && ` · 📧 ${t.emailTo}`}
                    </div>
                  </div>
                  <button onClick={(e) => { e.stopPropagation(); runTrigger(t.dataTriggerId, t.triggerName) }}
                    style={btnStyle('#238636', { fontSize: 12, padding: '4px 10px' })}>
                    Run ▶
                  </button>
                </div>
              </div>
            ))}
          </Card>

          {/* Cache Inspector */}
          <Card title="Redis Cache Inspector">
            <div style={{ display: 'flex', gap: 8, marginBottom: 10 }}>
              <button onClick={fetchCacheKeys} style={btnStyle('#30363d', { fontSize: 12 })}>Inspect Keys</button>
              <button onClick={flushCache} style={btnStyle('#da3633', { fontSize: 12 })}>Flush Cache</button>
            </div>
            {cacheKeys && (
              <div style={{ fontSize: 12 }}>
                <div style={{ color: '#8b949e', marginBottom: 5 }}>Total keys: {cacheKeys.totalKeys}</div>
                {cacheKeys.keys.map((k, i) => (
                  <div key={i} style={{ padding: '3px 0', borderBottom: '1px solid #21262d' }}>
                    <code style={{ color: '#79c0ff' }}>{k.key}</code>
                    <span style={{ color: '#8b949e', marginLeft: 8 }}>
                      ({k.type}, TTL: {k.ttlSeconds > 0 ? `${Math.round(k.ttlSeconds)}s` : 'no expiry'})
                    </span>
                  </div>
                ))}
              </div>
            )}
          </Card>
        </div>

        {/* Right: Executions + Logs */}
        <div style={{ flex: 1 }}>
          {selectedTrigger && (
            <Card title="Execution History">
              <button onClick={() => fetchExecutions(selectedTrigger)}
                style={btnStyle('#30363d', { marginBottom: 10, fontSize: 12 })}>
                Refresh
              </button>
              {executions.length === 0 ? (
                <div style={{ color: '#8b949e' }}>No executions yet. Click "Run ▶" on a trigger.</div>
              ) : (
                <table style={{ width: '100%', fontSize: 12, borderCollapse: 'collapse' }}>
                  <thead>
                    <tr style={{ borderBottom: '1px solid #30363d', color: '#8b949e' }}>
                      <th style={{ textAlign: 'left', padding: 4 }}>Status</th>
                      <th style={{ textAlign: 'left', padding: 4 }}>Created</th>
                      <th style={{ textAlign: 'left', padding: 4 }}>Started</th>
                      <th style={{ textAlign: 'left', padding: 4 }}>Completed</th>
                      <th style={{ textAlign: 'left', padding: 4 }}>Time</th>
                    </tr>
                  </thead>
                  <tbody>
                    {executions.map(e => (
                      <tr key={e.dataTriggerExecutionId} style={{ borderBottom: '1px solid #21262d' }}>
                        <td style={{ padding: 4 }}>
                          <StatusBadge status={e.status} />
                        </td>
                        <td style={{ padding: 4, color: '#8b949e' }}>{new Date(e.dateCreated).toLocaleTimeString()}</td>
                        <td style={{ padding: 4, color: '#8b949e' }}>{e.dateExecutionStarted ? new Date(e.dateExecutionStarted).toLocaleTimeString() : '-'}</td>
                        <td style={{ padding: 4, color: '#8b949e' }}>{e.dateExecutionCompleted ? new Date(e.dateExecutionCompleted).toLocaleTimeString() : '-'}</td>
                        <td style={{ padding: 4, color: '#8b949e' }}>{e.executionTimeSeconds ? `${e.executionTimeSeconds}s` : '-'}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              )}
            </Card>
          )}

          <Card title="Activity Log">
            <div style={{ maxHeight: 300, overflowY: 'auto', fontSize: 12, fontFamily: 'monospace' }}>
              {logs.length === 0 ? (
                <div style={{ color: '#8b949e' }}>Interact with the dashboard to see events here.</div>
              ) : logs.map((log, i) => (
                <div key={i} style={{ padding: '2px 0', color: log.includes('✓') ? '#3fb950' : log.includes('✗') ? '#f85149' : '#c9d1d9' }}>
                  {log}
                </div>
              ))}
            </div>
          </Card>
        </div>
      </div>
    </div>
  )
}

function Card({ title, children, style = {} }) {
  return (
    <div style={{ background: '#161b22', border: '1px solid #30363d', borderRadius: 8, padding: 16, marginBottom: 16, ...style }}>
      <h3 style={{ margin: '0 0 12px 0', fontSize: 14, color: '#8b949e', textTransform: 'uppercase', letterSpacing: 0.5 }}>{title}</h3>
      {children}
    </div>
  )
}

function Stat({ label, value }) {
  return (
    <div style={{ textAlign: 'center' }}>
      <div style={{ fontSize: 28, fontWeight: 'bold', color: '#f0f6fc' }}>{value}</div>
      <div style={{ fontSize: 11, color: '#8b949e' }}>{label}</div>
    </div>
  )
}

function StatusBadge({ status }) {
  const colors = {
    Healthy: '#3fb950', Success: '#3fb950',
    Pending: '#d29922',
    Failure: '#f85149', Unhealthy: '#f85149', Unreachable: '#f85149'
  }
  const color = colors[status] || '#8b949e'
  return (
    <span style={{ background: `${color}22`, color, padding: '2px 8px', borderRadius: 12, fontSize: 12, fontWeight: 600 }}>
      {status}
    </span>
  )
}

function btnStyle(bg, extra = {}) {
  return { background: bg, color: '#f0f6fc', border: 'none', padding: '6px 14px', borderRadius: 6, cursor: 'pointer', fontWeight: 500, ...extra }
}
