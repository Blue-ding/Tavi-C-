import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import '@xyflow/react/dist/style.css'
import './styles.css'
import App from './App'
import { ScenarioPage } from './ScenarioPage'

const page = window.location.pathname.replace(/\/+$/, '') === '/scenario' ? <ScenarioPage /> : <App />

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    {page}
  </StrictMode>,
)
