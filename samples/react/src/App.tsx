import { useState } from 'react'
import './App.css'
import { positronService } from './positronService'

function App() {
  const [name, setName] = useState('world')
  const [result, setResult] = useState('Result will appear here.')

  async function greet() {
    try {
      const res = await positronService.greet(name)
      setResult(res.message)
    } catch (e) {
      setResult('error: ' + (e as Error).message)
    }
  }

  return (
    <main style={{ font: '15px system-ui, sans-serif', margin: '2rem' }}>
      <h1 style={{ fontSize: '1.3rem' }}>Positron — React + Vite</h1>
      <p>
        <input
          value={name}
          aria-label="name"
          onChange={(e) => setName(e.target.value)}
          style={{ font: 'inherit', padding: '0.4rem 0.6rem' }}
        />{' '}
        <button onClick={greet} style={{ font: 'inherit', padding: '0.4rem 0.6rem' }}>
          Greet via C#
        </button>
      </p>
      <div style={{ marginTop: '1rem', padding: '0.75rem', border: '1px solid #8884', borderRadius: 6 }}>
        {result}
      </div>
    </main>
  )
}

export default App
