import { createRoot } from 'react-dom/client'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { BrowserRouter } from 'react-router-dom'
import { AuthProvider } from './context/AuthProvider'
import App from './App'
import '@fontsource-variable/geist'
import './index.css'
const cache=new QueryClient({defaultOptions:{queries:{retry:1,staleTime:15000,refetchOnWindowFocus:true}}})
createRoot(document.getElementById('root')).render(<QueryClientProvider client={cache}><AuthProvider><BrowserRouter basename={import.meta.env.BASE_URL.replace(/\/$/,'')}><App/></BrowserRouter></AuthProvider></QueryClientProvider>)
