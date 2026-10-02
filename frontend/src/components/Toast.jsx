import { useEffect, useState } from 'react'
export function Toast(){
  const [message,setMessage]=useState('')
  useEffect(()=>{let timer;const handler=e=>{setMessage(e.detail);clearTimeout(timer);timer=setTimeout(()=>setMessage(''),3500)};window.addEventListener('bench-toast',handler);return()=>{window.removeEventListener('bench-toast',handler);clearTimeout(timer)}},[])
  return message?<div role="status" className="toast">{message}</div>:null
}
