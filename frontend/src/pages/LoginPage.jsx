import { useState } from 'react'
import { Navigate } from 'react-router-dom'
import { ShieldCheck, QrCode, ArrowLeft, Download } from 'lucide-react'
import { api, errorText } from '../services/api'
import { useAuth } from '../context/auth-context'
import { Button, ErrorMessage, Field } from '../components/ui'
export default function LoginPage(){
  const {session,ready,accept}=useAuth()
  const [step,setStep]=useState('password'),[credentials,setCredentials]=useState({email:'',matKhau:''}),[code,setCode]=useState(''),[recoveryMode,setRecoveryMode]=useState(false),[setup,setSetup]=useState(null),[setupToken,setSetupToken]=useState(''),[result,setResult]=useState(null),[saved,setSaved]=useState(false),[busy,setBusy]=useState(false),[error,setError]=useState('')
  if(!ready)return <div className="auth-page">Đang kiểm tra phiên đăng nhập…</div>
  if(session)return <Navigate to="/devices" replace/>
  const back=()=>{setStep('password');setCode('');setError('');setCredentials(v=>({...v,matKhau:''}));setSetup(null);setSetupToken('');setResult(null);setSaved(false)}
  async function submit(e){
    e.preventDefault();setBusy(true);setError('')
    try{
      if(step==='setup'){
        const {data}=await api.post('/auth/totp/xac-nhan',{ma:code},{setupToken});setResult(data);setStep('recovery');setCode('')
      }else{
        const {data}=await api.post('/auth/login',{...credentials,...(step==='otp'?{[recoveryMode?'maKhoiPhuc':'maTotp']:code}:{})},{skipAuth:true})
        if(data.canGhiDanhTotp){setSetupToken(data.tokenGhiDanh);setStep('setup');const qr=await api.post('/auth/totp/ghi-danh',null,{setupToken:data.tokenGhiDanh});setSetup(qr.data)}
        else if(data.canMaTotp){setStep('otp');setCode('')}
        else accept(data)
      }
    }catch(e){setError(errorText(e))}finally{setBusy(false)}
  }
  const saveCodes=()=>{const url=URL.createObjectURL(new Blob([result.maKhoiPhuc.join('\n')],{type:'text/plain;charset=utf-8'}));const link=document.createElement('a');link.href=url;link.download='bench-console-ma-khoi-phuc.txt';link.click();setTimeout(()=>URL.revokeObjectURL(url),1000)}
  return <div className="auth-page"><div className="auth-brand"><div className="brand-icon"><ShieldCheck size={22}/></div><strong>Bench Console</strong><span>Remote Testing Platform</span></div><section className="auth-card"><div className="eyebrow">{step==='setup'?'THIẾT LẬP LẦN ĐẦU':step==='recovery'?'BẢO VỆ TÀI KHOẢN':'CHÀO MỪNG TRỞ LẠI'}</div><h1>{step==='password'?'Đăng nhập':step==='otp'?'Mã xác thực':step==='setup'?'Quét mã Authenticator':'Lưu mã khôi phục'}</h1><p className="auth-description">{step==='password'?'Đăng nhập để quản lý thiết bị và hoạt động kiểm thử.':step==='otp'?'Nhập mã có sẵn trong Microsoft Authenticator của bạn.':step==='setup'?'Quét QR bằng Microsoft Authenticator, rồi nhập mã 6 số để xác nhận. Các lần đăng nhập sau chỉ cần mã xác thực.':'Các mã chỉ hiển thị một lần. Lưu ở nơi riêng tư để dùng khi mất điện thoại.'}</p><ErrorMessage>{error}</ErrorMessage>
    {step==='recovery'?<><div className="recovery-grid">{result.maKhoiPhuc.map(value=><code key={value}>{value}</code>)}</div><Button onClick={saveCodes}><Download size={14}/>Tải mã khôi phục</Button><label className="check mt-5"><input type="checkbox" checked={saved} onChange={e=>setSaved(e.target.checked)}/>Tôi đã lưu mã khôi phục</label><Button className="w-full mt-5" variant="primary" disabled={!saved} onClick={()=>accept(result.phien)}>Vào ứng dụng</Button></>:<form onSubmit={submit}><fieldset disabled={busy} className="form-fields">
    {step==='password'?<><Field label="Email"><input type="email" autoComplete="username" required value={credentials.email} onChange={e=>setCredentials(v=>({...v,email:e.target.value}))}/></Field><Field label="Mật khẩu"><input type="password" autoComplete="current-password" required value={credentials.matKhau} onChange={e=>setCredentials(v=>({...v,matKhau:e.target.value}))}/></Field></>:<>
    {step==='setup'&&(setup?<><div className="qr-frame"><img src={setup.anhQr} alt="Mã QR thiết lập Microsoft Authenticator"/></div><details><summary>Nhập khoá bằng tay</summary><code className="secret-key">{setup.biMatChiaNhom}</code></details></>:<div className="empty"><QrCode size={28}/>Đang tải QR…</div>)}
    <Field label={recoveryMode&&step==='otp'?'Mã khôi phục':'Mã xác thực 6 số'}><input key={step+recoveryMode} required autoFocus autoComplete="one-time-code" inputMode={recoveryMode?'text':'numeric'} pattern={recoveryMode?undefined:'[0-9]{6}'} maxLength={recoveryMode?20:6} value={code} onChange={e=>setCode(e.target.value.trim())}/></Field></>}
    <Button type="submit" variant="primary" className="w-full" busy={busy} disabled={step==='setup'&&!setup}>{step==='password'?'Đăng nhập':'Xác nhận'}</Button></fieldset></form>}
    {step==='otp'&&<Button variant="ghost" className="mt-3" disabled={busy} onClick={()=>{setRecoveryMode(v=>!v);setCode('');setError('')}}>{recoveryMode?'Dùng Microsoft Authenticator':'Dùng mã khôi phục'}</Button>}{step!=='password'&&<Button variant="ghost" className="mt-3" disabled={busy} onClick={back}><ArrowLeft size={14}/>Quay lại đăng nhập</Button>}
    </section><p className="auth-footer">Bench · Vehicle · Component / ECU</p></div>
}
