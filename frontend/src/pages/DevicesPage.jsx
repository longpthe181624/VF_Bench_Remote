import { useRef, useState } from 'react'
import { Link, useNavigate, useParams, useSearchParams } from 'react-router-dom'
import { useQueryClient } from '@tanstack/react-query'
import { Plus, ArrowLeft, Pencil, ClipboardPlus } from 'lucide-react'
import { useAuth } from '../context/auth-context'
import { useApi } from '../hooks/use-api'
import { get, post, patch, remove, errorText } from '../services/api'
import { notify } from '../lib/notify'
import { date, matches, typeName, stateName, stateTone } from '../lib/format'
import { Badge, Button, DataTable, DetailList, ErrorMessage, Field, Modal, PageHeading, Toolbar } from '../components/ui'
import { MutationForm } from '../components/MutationForm'

function DeviceStatus({device}){return <Badge tone={device.hoTroRemote?stateTone(device.state):'neutral'}>{device.hoTroRemote?stateName[device.state]||device.state:'Không hỗ trợ remote'}</Badge>}
function DeviceForm({device,devices,projects,onClose}){
  const cache=useQueryClient(),created=useRef(device?.code)
  const [type,setType]=useState(device?.loai||'bench'),[remote,setRemote]=useState(device?.hoTroRemote??true)
  const [children,setChildren]=useState(devices.filter(d=>device&&d.thuocVeCode===device.code).map(d=>d.code))
  const toggle=code=>setChildren(v=>v.includes(code)?v.filter(c=>c!==code):[...v,code])
  async function save(form){
    const body=Object.fromEntries(form);body.hoTroRemote=remote;body.hoTroRobot=form.has('hoTroRobot');body.duAns=form.getAll('duAns');body.loai=type;body.thuocVe=type==='ecu'?body.thuocVe||'':'';body.code=body.code.trim().toUpperCase()
    const parent=created.current?await patch(`/devices/${encodeURIComponent(created.current)}`,body):await post('/devices',body)
    created.current=parent.code
    try{
      if(type!=='ecu'){
        const fresh=await get('/devices')
        for(const child of fresh.filter(d=>d.loai==='ecu')){
          const selected=children.includes(child.code),owned=child.thuocVeCode===parent.code
          if(selected!==owned&&(selected||owned))await patch(`/devices/${encodeURIComponent(child.code)}`,{thuocVe:selected?parent.code:''})
        }
      }
    }catch(e){await cache.invalidateQueries();throw Error(`Đã lưu thiết bị ${parent.code}, nhưng cập nhật ECU chưa hoàn tất: ${errorText(e)}. Kiểm tra rồi bấm Lưu để thử lại.`)}
    await cache.invalidateQueries();notify('Đã lưu thiết bị.')
  }
  return <MutationForm onSave={save} onClose={onClose}><div className="form-grid"><Field label="Loại đối tượng"><select name="loai" value={type} onChange={e=>setType(e.target.value)}>{Object.entries(typeName).map(([value,label])=><option key={value} value={value}>{label}</option>)}</select></Field><Field label={`Tên ${type==='vehicle'?'vehicle':type==='bench'?'bench':'ECU / thiết bị'} *`}><input name="ten" defaultValue={device?.ten||''} required maxLength={128}/></Field><Field label="Mã ID duy nhất *"><input name="code" defaultValue={device?.code||''} readOnly={!!device} required maxLength={64}/></Field><Field label="Dòng xe" hint={remote?'Bắt buộc khi hỗ trợ remote để định tuyến MQTT.':'Không bắt buộc với ECU độc lập.'}><input name="model" defaultValue={device?.model||''} required={remote}/></Field><Field label="Phòng"><input name="workshop" defaultValue={device?.workshop||''}/></Field><Field label="Tầng"><input name="tang" defaultValue={device?.tang||''}/></Field></div>
    <div className="check-group"><span className="field-label">Dự án sử dụng</span><div>{projects.map(p=><label className="check" key={p.ma}><input name="duAns" type="checkbox" value={p.ma} defaultChecked={device?.duAns?.includes(p.ma)}/>{p.ten}</label>)}</div></div>
    <div className="flex flex-wrap gap-5"><label className="check"><input type="checkbox" checked={remote} onChange={e=>setRemote(e.target.checked)}/>Hỗ trợ test remote</label><label className="check"><input name="hoTroRobot" type="checkbox" defaultChecked={device?.hoTroRobot}/>Hỗ trợ robot testing</label></div>
    {type==='ecu'?<Field label="Thuộc Bench / Vehicle"><select name="thuocVe" defaultValue={device?.thuocVeCode||''}><option value="">Đứng riêng</option>{devices.filter(d=>d.loai!=='ecu'&&d.code!==device?.code).map(d=><option key={d.code} value={d.code}>{d.ten||d.code} · {d.code}</option>)}</select></Field>:<div className="check-group"><span className="field-label">Danh sách ECU</span><div>{devices.filter(d=>d.loai==='ecu'&&(!d.thuocVeCode||d.thuocVeCode===device?.code)&&d.code!==device?.code).map(d=><label className="check" key={d.code}><input type="checkbox" checked={children.includes(d.code)} onChange={()=>toggle(d.code)}/>{d.ten||d.code} · {d.code}</label>)}</div><small>Chọn ECU đã đăng ký và chưa nằm trong Bench / Vehicle khác.</small></div>}
    <div className="form-grid">{remote&&<Field label="Tên máy tính"><input name="tenMay" defaultValue={device?.tenMay||''}/></Field>}<Field label="Phiên bản hiện tại"><input name="firmware" defaultValue={device?.firmware||''}/></Field><Field label="Vị trí / rack"><input name="rack" defaultValue={device?.rack||''}/></Field></div>
  </MutationForm>
}
export default function DevicesPage(){
  const {can}=useAuth(),navigate=useNavigate(),cache=useQueryClient()
  const devices=useApi('/devices','BENCH.VIEW',null,{refetchInterval:5000}),projects=useApi('/projects','BENCH.VIEW')
  const [q,setQ]=useState(''),[type,setType]=useState(''),[state,setState]=useState(''),[projectParams,setProjectParams]=useSearchParams(),[creating,setCreating]=useState(false),[error,setError]=useState('')
  async function action(d,verb){if(verb==='delete'&&!window.confirm(`Xoá ${d.ten||d.code}? ECU bên trong sẽ được tách ra.`))return;try{if(verb==='delete')await remove(`/devices/${encodeURIComponent(d.code)}`);else await post(`/devices/${encodeURIComponent(d.code)}/${verb}`);await cache.invalidateQueries();notify(verb==='delete'?'Đã xoá thiết bị.':'Đã gửi lệnh dừng tới client.')}catch(e){setError(errorText(e))}}
  const project=projectParams.get('project')||'',setProject=value=>setProjectParams(value?{project:value}:{})
  const rows=(devices.data||[]).filter(d=>matches(d,q,['code','ten','model','workshop','testCase'])&&(!type||d.loai===type)&&(!state||d.state===state)&&(!project||d.duAns?.includes(project)))
  return <><PageHeading title="Thiết bị" description="Quản lý Bench, Vehicle và Component / ECU."><Badge>{devices.data?.length||0} thiết bị</Badge></PageHeading><Toolbar value={q} onSearch={setQ} placeholder="Tìm tên / mã / phòng" actions={can('BENCH.CREATE')&&<Button variant="primary" onClick={()=>setCreating(true)}><Plus size={14}/>Đăng ký thiết bị</Button>}><select aria-label="Loại thiết bị" value={type} onChange={e=>setType(e.target.value)}><option value="">Mọi loại</option>{Object.entries(typeName).map(([v,t])=><option key={v} value={v}>{t}</option>)}</select><select aria-label="Trạng thái" value={state} onChange={e=>setState(e.target.value)}><option value="">Mọi trạng thái</option>{Object.entries(stateName).map(([v,t])=><option key={v} value={v}>{t}</option>)}</select><select aria-label="Dự án" value={project} onChange={e=>setProject(e.target.value)}><option value="">Mọi dự án</option>{(projects.data||[]).map(p=><option key={p.ma} value={p.ma}>{p.ten}</option>)}</select></Toolbar><ErrorMessage>{error||devices.error&&errorText(devices.error)}</ErrorMessage><DataTable key={q+type+state+project} loading={devices.isLoading} rows={rows} onRow={d=>navigate(`/devices/${encodeURIComponent(d.code)}`)} columns={[
    {key:'ten',label:'Thiết bị',render:d=><><strong>{d.ten||d.code}</strong><small>{d.code} · {[d.workshop,d.tang].filter(Boolean).join(' · ')||'Chưa có vị trí'}</small></>},
    {key:'loai',label:'Loại',render:d=>typeName[d.loai]}, {key:'model',label:'Dòng xe / dự án',render:d=><>{d.model||'—'}<small>{d.duAns?.join(', ')||'Chưa gán dự án'}</small></>}, {key:'state',label:'Trạng thái',render:d=><DeviceStatus device={d}/>},{key:'testCase',label:'Testcase'},{key:'lastSeenAt',label:'Cập nhật',render:d=>date(d.lastSeenAt)},
    {key:'actions',label:'Thao tác',render:d=><div className="actions">{can('BENCH.RUN')&&<Button onClick={()=>navigate(`/requests/new?device=${encodeURIComponent(d.code)}`)}>Tạo Request</Button>}{can('BENCH.RUN')&&d.hoTroRemote&&d.state==='running'&&<Button onClick={()=>action(d,'stop')}>Dừng</Button>}{can('BENCH.DELETE')&&<Button variant="danger" disabled={d.state==='running'} onClick={()=>action(d,'delete')}>Xoá</Button>}</div>},
  ]}/><Modal open={creating} onClose={()=>setCreating(false)} title="Đăng ký thiết bị" wide>{creating&&<DeviceForm devices={devices.data||[]} projects={projects.data||[]} onClose={()=>setCreating(false)}/>}</Modal></>
}
export function DeviceDetailPage(){
  const {code}=useParams(),{can}=useAuth(),cache=useQueryClient(),navigate=useNavigate()
  const device=useApi(`/devices/${encodeURIComponent(code)}`,'BENCH.VIEW',null,{refetchInterval:5000}),devices=useApi('/devices','BENCH.VIEW'),projects=useApi('/projects','BENCH.VIEW')
  const [tab,setTab]=useState('info'),[editing,setEditing]=useState(false),[error,setError]=useState('')
  const runs=useApi(`/devices/${encodeURIComponent(code)}/runs`,'REPORT.VIEW',null,{enabled:tab==='runs'&&can('REPORT.VIEW')})
  const telemetry=useApi(`/devices/${encodeURIComponent(code)}/telemetry`,'BENCH.VIEW',null,{enabled:tab==='telemetry'&&can('BENCH.VIEW'),refetchInterval:5000})
  const d=device.data
  if(!d)return <><Button onClick={()=>navigate('/devices')}><ArrowLeft size={14}/>Danh sách thiết bị</Button><ErrorMessage>{device.error&&errorText(device.error)}</ErrorMessage><div className="empty">{device.isLoading?'Đang tải thiết bị…':'Không tìm thấy thiết bị.'}</div></>
  return <><PageHeading title={d.ten||d.code} description={d.code}><Button onClick={()=>navigate('/devices')}><ArrowLeft size={14}/>Danh sách</Button>{can('BENCH.RUN')&&<Button variant="primary" onClick={()=>navigate(`/requests/new?device=${encodeURIComponent(d.code)}`)}><ClipboardPlus size={14}/>Tạo Request</Button>}{can('BENCH.UPDATE')&&<Button onClick={()=>setEditing(true)}><Pencil size={14}/>Sửa</Button>}{can('BENCH.RUN')&&d.hoTroRemote&&d.state==='running'&&<Button onClick={async()=>{try{await post(`/devices/${encodeURIComponent(code)}/stop`);await cache.invalidateQueries();notify('Đã gửi lệnh dừng.')}catch(e){setError(errorText(e))}}}>Dừng</Button>}</PageHeading><ErrorMessage>{error||device.error&&errorText(device.error)}</ErrorMessage><div className="tabs">{[['info','Thông tin'],...(can('REPORT.VIEW')?[['runs','Lịch sử chạy']]:[]),...(d.hoTroRemote?[['telemetry','Dữ liệu đo']]:[])].map(([v,t])=><Button variant="ghost" className={tab===v?'selected':''} key={v} onClick={()=>setTab(v)}>{t}</Button>)}</div>
    {tab==='info'?<div className="detail-grid"><section className="panel"><h3>Hồ sơ thiết bị</h3><DetailList items={[["Tên",d.ten],["Mã ID",d.code],["Loại",typeName[d.loai]],["Dòng xe",d.model||'—'],["Dự án",d.duAns?.join(', ')||'—'],["Phòng / tầng",[d.workshop,d.tang,d.rack].filter(Boolean).join(' · ')||'—'],["Thuộc thiết bị",d.thuocVeCode||'Đứng riêng'],["Tên máy",d.tenMay],["Phiên bản",d.firmware],["Hỗ trợ remote",d.hoTroRemote?'Có':'Không'],["Hỗ trợ robot",d.hoTroRobot?'Có':'Không']]}/></section><section className="panel"><h3>Trạng thái</h3><DetailList items={[["Hiện tại",<DeviceStatus key="status" device={d}/>],["Testcase",d.testCase],["Bước",d.step],["Ghi chú",d.note],["Cập nhật",date(d.lastSeenAt)]]}/><h3 className="mt-8">Danh sách ECU</h3><div className="child-list">{(d.chuaNhung||[]).map(child=><Link key={child.code} to={`/devices/${encodeURIComponent(child.code)}`}>{child.ten||child.code}<small>{child.code}</small></Link>)}{!d.chuaNhung?.length&&<p className="muted">Chưa có ECU bên trong.</p>}</div></section></div>:tab==='runs'?<><ErrorMessage>{runs.error&&errorText(runs.error)}</ErrorMessage><DataTable rows={Array.isArray(runs.data)?runs.data:runs.data?.items||[]} loading={runs.isLoading} columns={[{key:'testCase',label:'Testcase'},{key:'verdict',label:'Kết quả'},{key:'runBy',label:'Người chạy'},{key:'finishedAt',label:'Hoàn tất',render:r=>date(r.finishedAt)}]}/></>:<section className="panel"><h3>Dữ liệu đo từ client</h3><ErrorMessage>{telemetry.error&&errorText(telemetry.error)}</ErrorMessage><DetailList items={[["Kênh chính",d.primaryChannel],["Giá trị",d.primaryValue==null?'—':`${d.primaryValue} ${d.primaryUnit||''}`],["Cập nhật",date(d.lastSeenAt)]]}/><pre className="json-preview mt-5">{telemetry.isLoading?'Đang tải…':JSON.stringify(telemetry.data,null,2)}</pre></section>}
    <Modal open={editing} onClose={()=>setEditing(false)} title={`Sửa ${d.code}`} wide>{editing&&<DeviceForm device={d} devices={devices.data||[]} projects={projects.data||[]} onClose={()=>setEditing(false)}/>}</Modal></>
}
