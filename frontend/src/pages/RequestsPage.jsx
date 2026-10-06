import { useState } from 'react'
import { useNavigate, useParams, useSearchParams } from 'react-router-dom'
import { useQueryClient } from '@tanstack/react-query'
import { Plus, ArrowLeft, Download, Upload } from 'lucide-react'
import { useAuth } from '../context/auth-context'
import { useApi } from '../hooks/use-api'
import { post, patch, remove, download, errorText } from '../services/api'
import { notify } from '../lib/notify'
import { date, typeName } from '../lib/format'
import { Badge, Button, DataTable, DetailList, ErrorMessage, Field, Notice, PageHeading, Toolbar } from '../components/ui'
import { UploadDialog } from '../components/files/UploadDialog'
import { createRequestCode } from '../lib/request-code'
function loadDrafts(key){try{const value=JSON.parse(localStorage.getItem(key));return Array.isArray(value)?value:[]}catch{return[]}}
function exportRequest(request){const url=URL.createObjectURL(new Blob([JSON.stringify(request,null,2)],{type:'application/json'}));const a=document.createElement('a');a.href=url;a.download=request.code+'.json';a.click();setTimeout(()=>URL.revokeObjectURL(url),1000)}
const stateNames={draft:'Bản nháp',submitted:'Đã tạo lệnh',queued:'Chờ tool',claimed:'Tool đã nhận',running:'Đang chạy',interrupted:'Gián đoạn',completed:'Hoàn tất',failed:'Thất bại',cancelled:'Đã huỷ'}
const modeName=value=>value==='manual'?'Manual':'Tự động'
function RequestWizard({initial,devices,projects,packages,software,onSave,onClose,onStored}){
  const {user,can}=useAuth(),[step,setStep]=useState(0),[form,setForm]=useState(()=>({...initial,code:initial.code||createRequestCode(),createdAt:initial.createdAt||new Date().toISOString()})),[error,setError]=useState(''),[busy,setBusy]=useState(false),[uploading,setUploading]=useState(false)
  const device=devices.find(d=>d.code===form.device),chosen=packages.filter(p=>form.packageIds.includes(p.id)),steps=['Thông tin','Đối tượng / phần mềm','Testcase','Thời gian','Kiểm tra']
  const update=(key,value)=>setForm(v=>({...v,[key]:value}))
  function validate(current){
    if(current===0&&(!form.name.trim()||!form.project))return 'Nhập tên Request và chọn dự án.'
    if(current===1){if(!device)return 'Chọn đối tượng kiểm thử.';if(!device.duAns?.includes(form.project))return 'Thiết bị không thuộc dự án đã chọn.';if(form.mode==='auto'&&!device.hoTroRemote)return 'Đối tượng không hỗ trợ kiểm thử tự động từ xa.';if(form.flash&&!form.softwareId)return 'Chọn file phần mềm cần flash.'}
    if(current===2&&(!chosen.length||chosen.some(p=>(p.kieuTest||'auto')!==form.mode)))return 'Chọn gói testcase phù hợp với loại kiểm thử.'
    if(current===3&&form.timing==='scheduled'&&(!form.scheduledAt||new Date(form.scheduledAt+'+07:00')<=new Date()))return 'Chọn thời gian chạy trong tương lai (giờ Việt Nam).'
    return ''
  }
  const forward=()=>{const message=validate(step);if(message){setError(message);return}setError('');setStep(v=>v+1)}
  const draft=async()=>{if(!form.name.trim()){setError('Nhập tên Request để lưu nháp.');return}setBusy(true);setError('');try{await onSave(form);notify('Đã lưu bản nháp trên server.')}catch(e){setError(errorText(e))}finally{setBusy(false)}}
  const supported=form.mode==='auto'&&!form.flash&&chosen.length>0
  async function send(){for(let i=0;i<4;i++){const message=validate(i);if(message){setError(message);return}}setBusy(true);setError('');let saved;try{saved=await onSave(form,false);setForm(v=>({...v,code:saved.code,revision:saved.revision}));const response=await post(`/requests/${encodeURIComponent(saved.code)}/enqueue`,{revision:saved.revision});await onStored({...saved,state:response.state,revision:saved.revision+1});notify('Đã đưa vào hàng chờ cho tool.')}catch(e){setError(errorText(e));if(saved){await onStored(saved);notify(errorText(e))}}finally{setBusy(false)}}
  return <><PageHeading title="Tạo Request test" description="Chuẩn bị đối tượng, phần mềm và gói testcase."><Button disabled={busy} onClick={onClose}><ArrowLeft size={14}/>Danh sách Request</Button></PageHeading><div className="wizard-steps">{steps.map((label,i)=><div key={label} className={i===step?'current':i<step?'complete':''}><span>{i+1}</span>{label}</div>)}</div><section className="panel"><h3>{steps[step]}</h3><fieldset className="form-fields" disabled={busy}>
    {step===0?<><div className="form-grid"><Field label="Tên Request *"><input required value={form.name} onChange={e=>update('name',e.target.value)} maxLength={128}/></Field><Field label="Người yêu cầu"><input value={form.requester||user.email} readOnly/></Field><Field label="Dự án *"><select value={form.project} onChange={e=>setForm(v=>({...v,project:e.target.value,device:'',packageIds:[]}))}><option value="">Chọn dự án</option>{projects.map(p=><option key={p.ma} value={p.ma}>{p.ten}</option>)}</select></Field><Field label="Loại kiểm thử"><select value={form.mode} onChange={e=>setForm(v=>({...v,mode:e.target.value,packageIds:[],flash:false,softwareId:''}))}><option value="auto">Tự động</option><option value="manual">Manual</option></select></Field></div><Field label="Mô tả"><textarea rows={3} maxLength={2000} value={form.description} onChange={e=>update('description',e.target.value)}/></Field></>:step===1?<><Field label="Đối tượng chạy *"><select value={form.device} onChange={e=>update('device',e.target.value)}><option value="">Chọn Component / Bench / Vehicle</option>{devices.filter(d=>d.duAns?.includes(form.project)&&(form.mode==='manual'||d.hoTroRemote)).map(d=><option key={d.code} value={d.code}>{d.ten||d.code} · {d.code} · {typeName[d.loai]}</option>)}</select></Field>{device&&<DetailList items={[["Phòng / tầng",[device.workshop,device.tang].filter(Boolean).join(' · ')],["Phiên bản hiện tại",device.firmware],["Remote",device.hoTroRemote?'Có':'Không'],["Robot",device.hoTroRobot?'Có':'Không']]}/>}<label className="check mt-5"><input type="checkbox" checked={form.flash} onChange={e=>update('flash',e.target.checked)}/>Yêu cầu flash trước khi kiểm thử</label>{<Field label="File phần mềm yêu cầu"><select value={form.softwareId} onChange={e=>update('softwareId',Number(e.target.value))}><option value="">Chưa chọn / giữ phiên bản hiện tại</option>{software.map(s=><option key={s.id} value={s.id}>{s.ten} · {s.tenFile}</option>)}</select></Field>}<Notice>Yêu cầu flash chỉ được ghi nhận trong bản nháp; chưa thực thi.</Notice></>:step===2?<><div className="flex justify-between items-center mb-5"><p className="muted">Chọn gói ZIP đã được BE kiểm tra định dạng.</p>{can('TESTCASE.UPLOAD')&&<Button onClick={()=>setUploading(true)}><Upload size={14}/>Upload ZIP</Button>}</div><div className="case-picker">{packages.filter(p=>(p.kieuTest||'auto')===form.mode).map(p=><label className="case-option" key={p.id}><input type="checkbox" checked={form.packageIds.includes(p.id)} onChange={e=>setForm(v=>({...v,packageIds:e.target.checked?[...v.packageIds,p.id]:v.packageIds.filter(id=>id!==p.id)}))}/><div><strong>{p.ten}</strong><small>{p.tenFileGoc} · {p.soTestCase} {p.kieuTest==='manual'?'file Excel':'testcase'}</small></div><Badge>{modeName(p.kieuTest||'auto')}</Badge></label>)}{!packages.some(p=>(p.kieuTest||'auto')===form.mode)&&<div className="empty">Chưa có gói phù hợp. Upload ZIP để tiếp tục.</div>}</div><Notice>Tool nhận các gói đã chọn và kiểm tra SHA256 trước khi chạy.</Notice></>:step===3?<><Field label="Thời gian chạy"><select value={form.timing} onChange={e=>update('timing',e.target.value)}><option value="now">Chạy ngay</option><option value="scheduled">Hẹn giờ</option></select></Field>{form.timing==='scheduled'&&<><Field label="Ngày / giờ chạy (Việt Nam)"><input type="datetime-local" value={form.scheduledAt} onChange={e=>update('scheduledAt',e.target.value)}/></Field><Notice>Tool được nhận việc từ giờ đã chọn khi thiết bị sẵn sàng.</Notice></>}</>:<><DetailList items={[["Tên Request",form.name],["Người yêu cầu",form.requester||user.email],["Dự án",form.project],["Loại",modeName(form.mode)],["Đối tượng",device?.ten||form.device],["Phần mềm",form.softwareId?software.find(s=>s.id===form.softwareId)?.ten:'Giữ phiên bản hiện tại'],["Gói testcase",chosen.map(p=>p.ten).join(', ')],["Thời gian",form.timing==='scheduled'?form.scheduledAt.replace('T',' ')+' (Việt Nam)':'Chạy ngay']]}/>{!supported&&<Notice>Yêu cầu này có tính năng đang phát triển. Lưu nháp và xuất JSON để giữ các thông tin đã chuẩn bị.</Notice>}</>}
    </fieldset><ErrorMessage>{error}</ErrorMessage><div className="form-footer">{step>0&&<Button disabled={busy} onClick={()=>{setError('');setStep(v=>v-1)}}>Quay lại</Button>}<Button disabled={busy} onClick={draft}>Lưu nháp</Button>{step<4?<Button disabled={busy} variant="primary" onClick={forward}>Tiếp tục</Button>:supported&&can('BENCH.RUN')?<Button variant="primary" busy={busy} onClick={send}>Gửi yêu cầu cho tool</Button>:<Button onClick={()=>exportRequest(form)}><Download size={14}/>Xuất yêu cầu JSON</Button>}</div></section>{uploading&&<UploadDialog kind="package" onlyTestcase onClose={()=>setUploading(false)}/>}</>
}
function requestBody(form) {
  return { name: form.name, project: form.project || '', device: form.device || '', mode: form.mode,
    description: form.description || '', flash: !!form.flash, softwareId: form.softwareId || null,
    packageIds: form.packageIds || [], timing: form.timing,
    scheduledAt: form.timing === 'scheduled' && form.scheduledAt ? form.scheduledAt + '+07:00' : null,
    revision: form.revision }
}
function editableRequest(request) {
  const scheduledAt = request.scheduledAt
    ? new Date(new Date(request.scheduledAt).getTime() + 7 * 3600000).toISOString().slice(0, 16) : ''
  return { ...request, softwareId: request.softwareId || '', scheduledAt }
}
export default function RequestsPage() {
  const {user,can}=useAuth(), navigate=useNavigate(), cache=useQueryClient(), {requestId}=useParams(), [params]=useSearchParams()
  const key=`benchconsole.requests.${user.id}.${user.email}`
  const [legacy,setLegacy]=useState(()=>loadDrafts(key)), [q,setQ]=useState(''), [page,setPage]=useState(1), [error,setError]=useState(''), [busy,setBusy]=useState(false)
  const list=useApi('/requests','REQUEST.VIEW',{q,page,size:50},{enabled:!requestId&&can('REQUEST.VIEW')})
  const code=requestId==='new'?params.get('draft'):requestId
  const detail=useApi(`/requests/${encodeURIComponent(code||'')}`,'REQUEST.VIEW',null,{enabled:!!code&&can('REQUEST.VIEW'),refetchInterval:5000})
  const selected=detail.data
  const devices=useApi('/devices','BENCH.VIEW'), projects=useApi('/projects','BENCH.VIEW'), packages=useApi('/test-cases','TESTCASE.VIEW',{loai:'testcase'}), software=useApi('/du-lieu-chung','DULIEU.VIEW',{loai:'phien-ban'})
  const jobs=useApi(`/requests/${encodeURIComponent(code||'')}/jobs`,'REQUEST.VIEW',null,{enabled:!!code&&can('REQUEST.VIEW'),refetchInterval:5000}),job=jobs.data?.[0]
  const results=useApi('/runs','REPORT.VIEW',{plan:selected?.code,size:200},{enabled:!!selected&&selected.state!=='draft'&&can('REPORT.VIEW'),refetchInterval:5000})
  async function stored(row) {
    cache.setQueryData([user.id,`/requests/${row.code}`,null],row)
    await cache.invalidateQueries({predicate:query=>query.queryKey[1]?.startsWith('/requests')})
    navigate(`/requests/${row.code}`)
  }
  async function save(form,open=true) {
    const row=form.revision?await patch(`/requests/${form.code}`,requestBody(form)):await post('/requests',requestBody(form))
    if(open)await stored(row)
    return row
  }
  async function deleteDraft(row) {
    if(!window.confirm(`Xoá Request nháp ${row.name}?`))return
    setBusy(true);setError('')
    try{await remove(`/requests/${row.code}?revision=${row.revision}`);await cache.invalidateQueries({predicate:query=>query.queryKey[1]?.startsWith('/requests')});notify('Đã xoá bản nháp.')}catch(e){setError(errorText(e))}finally{setBusy(false)}
  }
  async function importDrafts() {
    setBusy(true);setError('');let remaining=[...legacy],count=0
    try {
      for(const draft of legacy.filter(r=>r.state==='draft')) {
        await post('/requests',requestBody(draft))
        remaining=remaining.filter(r=>r!==draft);localStorage.setItem(key,JSON.stringify(remaining));setLegacy(remaining);count++
      }
      notify(`Đã nhập ${count} bản nháp vào server.`)
    }catch(e){setError(`Đã nhập ${count} bản nháp. Các bản chưa nhập được giữ trên trình duyệt: ${errorText(e)}`)}
    finally{await cache.invalidateQueries({predicate:query=>query.queryKey[1]?.startsWith('/requests')});setBusy(false)}
  }
  if(!can('REQUEST.VIEW'))return <Notice>Bạn không có quyền xem Request.</Notice>
  if(code&&detail.isLoading)return <div className="empty">Đang tải Request…</div>
  if(code&&detail.error)return <ErrorMessage>{errorText(detail.error)}</ErrorMessage>
  if(requestId==='new') {
    if(code&&!selected?.canEdit)return <Notice>Bạn không thể sửa Request này.</Notice>
    if(!code&&!can('REQUEST.CREATE'))return <Notice>Bạn không có quyền tạo Request.</Notice>
    if(devices.isLoading||projects.isLoading||packages.isLoading||software.isLoading)return <div className="empty">Đang tải dữ liệu tạo Request…</div>
    const target=devices.data?.find(d=>d.code===params.get('device'))
    const initial=selected?editableRequest(selected):{code:'',name:'',requester:user.email,project:target?.duAns?.[0]||projects.data?.[0]?.ma||'',mode:target&&!target.hoTroRemote?'manual':'auto',device:target?.code||'',description:'',flash:false,softwareId:'',packageIds:[],timing:'now',scheduledAt:'',createdAt:''}
    // Dùng snapshot đã lưu để sửa nháp không tự đổi sang bytes mới của file nguồn.
    const pinnedPackages=(selected?.files||[]).filter(f=>f.kind==='package').map(f=>({id:f.sourceId,ten:f.name,tenFileGoc:f.fileName,sha256:f.sha256,kieuTest:f.mode}))
    const pinnedSoftware=(selected?.files||[]).filter(f=>f.kind==='software').map(f=>({id:f.sourceId,ten:f.name,tenFile:f.fileName,sha256:f.sha256}))
    const choices=[...pinnedPackages,...(packages.data||[]).filter(p=>!pinnedPackages.some(f=>f.id===p.id))]
    const softwareChoices=[...pinnedSoftware,...(software.data||[]).filter(p=>!pinnedSoftware.some(f=>f.id===p.id))]
    return <><ErrorMessage>{[devices.error,projects.error,packages.error,software.error].filter(Boolean).map(errorText).join(' ')}</ErrorMessage><RequestWizard key={selected?.code||params.toString()} initial={initial} devices={devices.data||[]} projects={projects.data||[]} packages={choices} software={softwareChoices} onSave={save} onStored={stored} onClose={()=>navigate('/requests')}/></>
  }
  if(requestId&&!selected)return <Notice>Request không tồn tại.</Notice>
  if(selected)return <>
    <PageHeading title={selected.name} description={selected.code}><Button onClick={()=>navigate('/requests')}><ArrowLeft size={14}/>Danh sách</Button>{selected.canEdit&&<Button onClick={()=>navigate(`/requests/new?draft=${selected.code}`)}>Sửa nháp</Button>}<Button onClick={()=>exportRequest(selected)}><Download size={14}/>Xuất JSON</Button></PageHeading>
    <Notice>Request đã lưu trên server. {selected.state!=='draft'?'Theo dõi trạng thái và kết quả từ tool.':'Bản nháp chưa gửi tới tool.'}</Notice>
    <ErrorMessage>{selected.commandError || jobs.error && errorText(jobs.error)}</ErrorMessage>
    {job && <section className="panel"><DetailList items={[["Việc",job.code],["Tiến độ",`${job.progress}%`],["Kết quả đã nhận",job.resultCount],["Thông tin tool",job.message],["Sớm nhất",date(job.notBefore)]]}/>{can('BENCH.RUN') && (user.email===selected.requester || user.vaiTro?.includes('Admin')) && (job.state==='queued' || job.state==='interrupted') && <Button busy={busy} onClick={async()=>{
      let reason='',hardwareStopped=false
      if(job.state==='interrupted'){if(!window.confirm('Đã kiểm tra tool và xác nhận phần cứng dừng hoàn toàn?'))return;hardwareStopped=true;reason=window.prompt('Lý do kết thúc việc gián đoạn:')||'';if(!reason.trim())return}
      else if(!window.confirm('Huỷ việc đang chờ tool?'))return
      setBusy(true);setError('');try{await post(`/requests/${selected.code}/jobs/${job.code}/${hardwareStopped?'resolve':'cancel'}`,{revision:job.revision,hardwareStopped,reason});await cache.invalidateQueries({predicate:query=>query.queryKey[1]?.startsWith('/requests')})}catch(e){setError(errorText(e))}finally{setBusy(false)}
    }}>{job.state==='queued'?'Huỷ việc chờ':'Xác nhận phần cứng đã dừng'}</Button>}</section>}
    <section className="panel"><DetailList items={[["Người yêu cầu",selected.requester],["Dự án",selected.project],["Loại",modeName(selected.mode)],["Đối tượng",selected.device],["Gói testcase",selected.packages?.map(p=>p.ten).join(', ')],["Thời gian",selected.timing==='scheduled'?date(selected.scheduledAt):'Chạy ngay'],["Trạng thái",stateNames[selected.state]||selected.state],["Trạng thái lệnh",selected.commandStatus],["Mã lệnh",selected.cmdId],["Tạo lúc",date(selected.createdAt)],["Cập nhật",date(selected.updatedAt)]]}/></section>
    <DataTable rows={selected.files||[]} columns={[{key:'name',label:'File đã chọn'},{key:'kind',label:'Loại',render:f=>f.kind==='package'?'Testcase':'Phần mềm'},{key:'fileName',label:'Tên file'},{key:'sha256',label:'SHA256',render:f=><small title={f.sha256}>{f.sha256.slice(0,16)}…</small>},{key:'actions',label:'Thao tác',render:f=><Button disabled={!can(f.kind==='package'?'TESTCASE.VIEW':'DULIEU.VIEW')} onClick={async()=>{try{await download(`/requests/${selected.code}/files/${f.id}/download`,f.fileName)}catch(e){setError(errorText(e))}}}><Download size={14}/>Tải về</Button>}]}/><ErrorMessage>{error}</ErrorMessage>
    {selected.state!=='draft'&&<><PageHeading title="Kết quả từ client"/><ErrorMessage>{results.error&&errorText(results.error)}</ErrorMessage><DataTable rows={results.data?.items||[]} loading={results.isLoading} empty={can('REPORT.VIEW')?'Chưa nhận được kết quả từ client.':'Bạn không có quyền xem báo cáo.'} columns={[{key:'testCase',label:'Testcase'},{key:'verdict',label:'Kết quả'},{key:'reason',label:'Lý do'},{key:'finishedAt',label:'Hoàn tất',render:r=>date(r.finishedAt)}]}/></>}
  </>
  return <><PageHeading title="Request test" description="Yêu cầu kiểm thử được lưu trên server và chia sẻ theo quyền truy cập."/><Notice>Request tự động được giao cho tool qua hàng chờ và lịch chạy. Manual và flash hiện lưu nháp.</Notice>
    {legacy.length>0&&<Notice>Còn {legacy.length} Request cũ trên trình duyệt. {legacy.some(r=>r.state==='draft')&&can('REQUEST.CREATE')&&<Button busy={busy} onClick={importDrafts}>Nhập nháp cũ vào server</Button>} <Button onClick={()=>exportRequest({code:'requests-cu',requests:legacy})}>Xuất dữ liệu cũ</Button></Notice>}
    <Toolbar value={q} onSearch={value=>{setQ(value);setPage(1)}} placeholder="Tìm mã / tên Request" actions={can('REQUEST.CREATE')&&<Button variant="primary" onClick={()=>navigate('/requests/new')}><Plus size={14}/>Tạo Request</Button>}/>
    <ErrorMessage>{error||list.error&&errorText(list.error)}</ErrorMessage>
    <DataTable rows={list.data?.items||[]} loading={list.isLoading} pageSize={50} footer={false} keyOf={r=>r.code} onRow={r=>navigate(`/requests/${r.code}`)} columns={[{key:'name',label:'Request',render:r=><><strong>{r.name}</strong><small>{r.code}</small></>},{key:'requester',label:'Người yêu cầu'},{key:'device',label:'Đối tượng'},{key:'mode',label:'Loại',render:r=>modeName(r.mode)},{key:'state',label:'Trạng thái',render:r=><Badge tone={r.cmdId?'info':'neutral'}>{stateNames[r.state]||r.state}</Badge>},{key:'createdAt',label:'Tạo lúc',render:r=>date(r.createdAt)},{key:'actions',label:'Thao tác',render:r=>r.canDelete&&<Button disabled={busy} variant="danger" onClick={()=>deleteDraft(r)}>Xoá nháp</Button>}]}/>
    <div className="form-footer"><span>{list.data?.total||0} Request · Trang {page}</span><Button disabled={page===1||list.isFetching} onClick={()=>setPage(p=>p-1)}>Trước</Button><Button disabled={page*50>=(list.data?.total||0)||list.isFetching} onClick={()=>setPage(p=>p+1)}>Sau</Button></div>
  </>
}
