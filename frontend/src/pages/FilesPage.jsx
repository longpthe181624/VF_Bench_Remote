import { useEffect, useRef, useState } from 'react'
import { Navigate, useLocation, useNavigate, useSearchParams } from 'react-router-dom'
import { useQueryClient } from '@tanstack/react-query'
import { Plus, Upload, Download, FolderCog } from 'lucide-react'
import { useAuth } from '../context/auth-context'
import { useApi } from '../hooks/use-api'
import { api, download, upload, patch, post, remove, errorText } from '../services/api'
import { notify } from '../lib/notify'
import { bytes, date, matches } from '../lib/format'
import { Badge, Button, DataTable, DetailList, ErrorMessage, Field, Modal, Notice, PageHeading, Toolbar } from '../components/ui'
import { MutationForm } from '../components/MutationForm'
import { SoftwareTypeSelect } from './SoftwareTypesPage'
import DatabasePage from './DatabasePage'
const privateConfig={title:'Kho cá nhân',description:'File riêng của bạn. Các tài khoản khác, kể cả Admin, không truy cập được.',path:'/storage',view:'KHO.VIEW',upload:'KHO.UPLOAD',delete:'KHO.DELETE'}
const sharedConfig={title:'Dữ liệu chung',description:'Tài liệu và file dùng chung của nhóm, phân theo mục.',path:'/du-lieu-chung',view:'DULIEU.VIEW',upload:'DULIEU.UPLOAD',delete:'DULIEU.DELETE'}
const packageConfig={title:'Gói / cấu hình',description:'Lưu ZIP testcase tự động, Excel manual và cấu hình client.',path:'/test-cases',view:'TESTCASE.VIEW',upload:'TESTCASE.UPLOAD',delete:'TESTCASE.DELETE'}
const downloadPath=(kind,file)=>kind==='private'?`/storage/files/${file.id}/download`:kind==='package'?`/test-cases/${file.id}/download`:kind==='report'?`/runs/reports/${file.id}/download`:`/du-lieu-chung/${file.id}/download`
const filename=(kind,file)=>kind==='package'?file.ten+'.zip':file.tenFile
export function DownloadButton({kind,file}){
  const [busy,setBusy]=useState(false)
  return <Button busy={busy} onClick={async()=>{setBusy(true);try{await download(downloadPath(kind,file),filename(kind,file))}catch(e){notify(errorText(e))}finally{setBusy(false)}}}><Download size={13}/>Tải về</Button>
}
export function FileDetailModal({file,kind,categories=[],onClose}){
  const {can}=useAuth(),cache=useQueryClient(),[editCategory,setEditCategory]=useState(file.loai)
  const editable=kind==='private'?can('KHO.UPLOAD'):kind==='shared'&&can('DULIEU.UPLOAD')
  const [preview,setPreview]=useState(null),[previewBusy,setPreviewBusy]=useState(false),[error,setError]=useState('')
  const controller=useRef(null),blobUrl=useRef(null)
  useEffect(()=>()=>{controller.current?.abort();if(blobUrl.current)URL.revokeObjectURL(blobUrl.current)},[])
  async function showPreview(){
    setError('');setPreviewBusy(true)
    try{
      const name=file.tenFile||file.tenFileGoc,ext=name.split('.').pop().toLowerCase(),images={png:'image/png',jpg:'image/jpeg',jpeg:'image/jpeg',gif:'image/gif',webp:'image/webp',bmp:'image/bmp'}
      if(!images[ext]&&!['txt','log','csv','dbc','json','xml','md','yaml','yml','ini','cfg'].includes(ext))throw Error('Tải xuống để mở định dạng này bằng ứng dụng phù hợp.')
      if(file.kichThuoc>2*1024**2)throw Error('Xem trước hỗ trợ file tối đa 2 MB. Hãy tải file lớn xuống.')
      controller.current=new AbortController()
      const {data}=await api.get(downloadPath(kind,file),{responseType:'blob',signal:controller.current.signal})
      if(data.size>2*1024**2)throw Error('File quá lớn để xem trước.')
      if(images[ext]){if(blobUrl.current)URL.revokeObjectURL(blobUrl.current);blobUrl.current=URL.createObjectURL(new Blob([data],{type:images[ext]}));setPreview({image:blobUrl.current})}else setPreview({text:await data.text()})
    }catch(e){setError(errorText(e))}finally{setPreviewBusy(false)}
  }
  async function save(form){const body=kind==='shared'?{ten:form.get('ten'),loai:form.get('loai'),moTa:form.get('moTa'),softwareTypeId:Number(form.get('softwareTypeId'))||0}:{tenFile:form.get('tenFile'),moTa:form.get('moTa')};await patch(kind==='shared'?`/du-lieu-chung/${file.id}`:`/storage/files/${file.id}`,body);await cache.invalidateQueries();notify('Đã lưu thông tin file.')}
  const fields=<><div className="form-grid"><Field label={kind==='shared'?'Tên hiển thị':'Tên file'}><input name={kind==='shared'?'ten':'tenFile'} defaultValue={kind==='shared'?file.ten:file.tenFile} maxLength={kind==='shared'?128:260} required readOnly={!editable}/></Field>{kind==='shared'&&<Field label="Mục lưu trữ"><select name="loai" value={editCategory} onChange={e=>setEditCategory(e.target.value)} disabled={!editable}>{categories.map(c=><option key={c.ma} value={c.ma}>{c.ten}</option>)}</select></Field>}</div>{kind==='shared'&&editCategory==='phien-ban'&&<SoftwareTypeSelect defaultValue={file.softwareTypeId} disabled={!editable} required={editable}/>}<Field label="Mô tả"><textarea name="moTa" defaultValue={file.moTa||''} readOnly={!editable} maxLength={512} rows={3}/></Field></>
  return <Modal open onClose={onClose} title={file.ten||file.tenFile} wide><DetailList items={[["File gốc",file.tenFile||file.tenFileGoc],["Dung lượng",bytes(file.kichThuoc)],["Người tải lên",file.nguoiTaiLen||file.nguoiDung||file.benchCode],["Thời gian",date(file.taiLenLuc||file.nhanLuc)],["SHA-256",<code className="break-all" key="sha">{file.sha256}</code>]]}/><div className="flex gap-2 my-5"><Button busy={previewBusy} onClick={showPreview}>Xem trước</Button><DownloadButton file={file} kind={kind}/></div><ErrorMessage>{error}</ErrorMessage>{preview?.image&&<img className="file-preview-image" alt={file.tenFile||file.tenFileGoc} src={preview.image}/>} {preview?.text!==undefined&&<pre className="json-preview">{preview.text}</pre>}
    {editable?<MutationForm onSave={save} onClose={onClose}>{fields}</MutationForm>:<>{kind!=='package'&&kind!=='report'&&fields}<div className="form-footer"><Button onClick={onClose}>Đóng</Button></div></>}
  </Modal>
}
export function UploadDialog({kind,category,categories,onClose,onlyTestcase=false}){
  const {can}=useAuth(),cache=useQueryClient(),controller=useRef(null)
  const [busy,setBusy]=useState(false),[progress,setProgress]=useState(0),[error,setError]=useState(''),[packageType,setPackageType]=useState(can('TESTCASE.UPLOAD')?'testcase':'config'),[uploadCategory,setUploadCategory]=useState(category||'khac')
  useEffect(()=>()=>controller.current?.abort(),[])
  async function save(e){
    e.preventDefault();setBusy(true);setError('');setProgress(0)
    const form=new FormData(e.currentTarget)
    controller.current=new AbortController()
    try{await upload(kind==='private'?'/storage':kind==='package'?'/test-cases':'/du-lieu-chung',form,{signal:controller.current.signal,progress:setProgress});await cache.invalidateQueries();notify('Đã tải lên và lưu file.');onClose()}catch(e){setError(errorText(e))}finally{setBusy(false)}
  }
  return <Modal open onClose={()=>{controller.current?.abort();onClose()}} title={kind==='package'?'Tải gói ZIP lên':'Tải file lên'}><form onSubmit={save}><fieldset disabled={busy} className="form-fields">
    {kind==='shared'&&<><Field label="Mục"><select name="loai" value={uploadCategory} onChange={e=>setUploadCategory(e.target.value)}>{categories.filter(c=>c.ma!=='dbc').map(c=><option key={c.ma} value={c.ma}>{c.ten}</option>)}</select></Field><Field label="Tên hiển thị" hint="Để trống để dùng tên file."><input name="ten" maxLength={128}/></Field></>}
    {kind==='shared'&&uploadCategory==='phien-ban'&&<SoftwareTypeSelect required/>}
    {kind==='package'&&<><Field label="Loại gói"><select name="loai" value={packageType} onChange={e=>setPackageType(e.target.value)}>{can('TESTCASE.UPLOAD')&&<option value="testcase">Testcase</option>}{!onlyTestcase&&can('CONFIG.UPLOAD')&&<option value="config">Cấu hình Qauto</option>}</select></Field>{packageType==='testcase'&&<Field label="Loại kiểm thử"><select name="kieuTest" defaultValue="auto"><option value="auto">Tự động · .tc / .mtc</option><option value="manual">Manual · Excel .xlsx / .xls</option></select></Field>}<Field label="Tên gói *" hint="Tên thư mục cho gói tự động; không dùng dấu cách hoặc ký tự đường dẫn."><input name="ten" required maxLength={128}/></Field></>}
    <Field label={kind==='private'?'Chọn file *':'File *'} hint={kind==='package'?'ZIP tối đa 64 MB. Manual cần Excel; tự động cần .tc / .mtc.':kind==='private'?'Tối đa 100 file; tổng dung lượng 256 MB mỗi lần.':'Một file tối đa 256 MB.'}><input name="file" type="file" required multiple={kind==='private'} accept={kind==='package'?'.zip':undefined}/></Field>{kind!=='package'&&<Field label="Mô tả"><textarea name="moTa" maxLength={512} rows={3}/></Field>}</fieldset><ErrorMessage>{error}</ErrorMessage>{busy&&<div className="upload-progress" role="status"><progress max={100} value={progress}/><span>{progress===100?'Đang kiểm tra và lưu file…':`Đã gửi ${progress}%`}</span></div>}<div className="form-footer"><Button onClick={()=>busy?controller.current?.abort():onClose()}>{busy?'Huỷ tải':'Huỷ'}</Button><Button type="submit" variant="primary" busy={busy}><Upload size={14}/>Tải lên</Button></div></form></Modal>
}
function SharedDataTabs(){
  const {can}=useAuth(),location=useLocation(),navigate=useNavigate(),[params]=useSearchParams(),categories=useApi('/du-lieu-chung/muc','DULIEU.VIEW'),database=useApi('/database/files','DULIEU.VIEW',{page:1,size:1})
  const category=params.get('category')||''
  const items=[['/testcases','Testcase','TESTCASE.VIEW'],['/software','Phiên bản phần mềm','DULIEU.VIEW'],['/packages','Gói / cấu hình','TESTCASE.VIEW']]
  return <div className="tabs">{items.filter(item=>can(item[2])).map(([path,label])=><Button variant="ghost" className={location.pathname===path||(path==='/software'&&category==='phien-ban')?'selected':''} key={path} onClick={()=>navigate(path)}>{label}</Button>)}{can('DULIEU.VIEW')&&(categories.data||[]).filter(c=>c.ma!=='phien-ban').map(c=><Button variant="ghost" className={location.pathname==='/files'&&category===c.ma?'selected':''} key={c.ma} onClick={()=>navigate('/files?category='+encodeURIComponent(c.ma))}>{c.ten} <span className="tab-count">{c.ma==='dbc'?database.data?.total||0:c.soFile}</span></Button>)}</div>
}
export default function FilesPage(props){
  const [params]=useSearchParams()
  if((props.kind||'shared')==='shared'&&!props.software&&!params.get('category'))return <Navigate to="/files?category=tai-lieu" replace/>
  if((props.kind||'shared')==='shared'&&!props.software&&params.get('category')==='dbc')return <><PageHeading title="Dữ liệu chung" description="Tài liệu và file dùng chung của nhóm, phân theo mục."/><SharedDataTabs/><DatabasePage embedded/></>
  return <FilesListPage {...props}/>
}
function FilesListPage({kind='shared',software=false,onlyTestcase=false}){
  const {can}=useAuth(),cache=useQueryClient(),navigate=useNavigate(),[params,setParams]=useSearchParams()
  const config=kind==='private'?privateConfig:kind==='package'?packageConfig:sharedConfig
  const categories=useApi('/du-lieu-chung/muc','DULIEU.VIEW')
  const category=software?'phien-ban':params.get('category')||''
  const softwareTypes=useApi('/software/types','DULIEU.VIEW',null,{enabled:kind==='shared'&&can('DULIEU.VIEW')})
  const softwareTypeId=category==='phien-ban'?params.get('softwareTypeId')||'':''
  const files=useApi(config.path,config.view,kind==='shared'?{loai:category||undefined,softwareTypeId:softwareTypeId||undefined}:kind==='package'&&onlyTestcase?{loai:'testcase'}:undefined)
  const devices=useApi('/devices','BENCH.VIEW',null,{enabled:kind==='package'&&can('BENCH.VIEW')})
  const [q,setQ]=useState(''),[type,setType]=useState(''),[uploading,setUploading]=useState(false),[selected,setSelected]=useState(null),[deploying,setDeploying]=useState(null),[error,setError]=useState('')
  const canUpload=kind==='package'?can('TESTCASE.UPLOAD')||(!onlyTestcase&&can('CONFIG.UPLOAD')):can(config.upload)
  const rows=(files.data||[]).filter(f=>matches(f,q,['ten','tenFile','tenFileGoc','moTa','nguoiTaiLen'])&&(!type||f.loai===type))
  async function deleteFile(file){if(!window.confirm(`Xoá ${file.ten||file.tenFile}?`))return;try{await remove(kind==='private'?`/storage/files/${file.id}`:`${config.path}/${file.id}`);await cache.invalidateQueries();notify('Đã xoá file.')}catch(e){setError(errorText(e))}}
  return <><PageHeading title={software?'Phiên bản phần mềm':onlyTestcase?'Testcase':config.title} description={software?'Kho file phần mềm và ghi chú phiên bản dùng chung.':config.description}><Badge>{files.data?.length||0} file</Badge></PageHeading>{(kind==='shared'||kind==='package')&&<SharedDataTabs/>}
    <Toolbar value={q} onSearch={setQ} placeholder="Tìm tên / file / mô tả" actions={<>{kind==='shared'&&category!=='phien-ban'&&can('DULIEU.MUC')&&<Button onClick={()=>navigate('/categories')}><FolderCog size={14}/>Quản lý mục</Button>}{canUpload&&<Button variant="primary" onClick={()=>setUploading(true)}><Upload size={14}/>Tải lên</Button>}</>}>{category==='phien-ban'&&<select aria-label="Type phần mềm" value={softwareTypeId} onChange={e=>{const next=new URLSearchParams(params);e.target.value?next.set('softwareTypeId',e.target.value):next.delete('softwareTypeId');setParams(next)}}><option value="">ALL — Tất cả Type</option><option value="0">Chưa phân loại</option>{(softwareTypes.data||[]).map(t=><option key={t.id} value={t.id}>{t.ten}</option>)}</select>}{kind==='package'&&!onlyTestcase&&<select aria-label="Loại gói" value={type} onChange={e=>setType(e.target.value)}><option value="">Mọi loại</option><option value="testcase">Testcase</option><option value="config">Cấu hình</option></select>}</Toolbar><ErrorMessage>{error||files.error&&errorText(files.error)}</ErrorMessage>
    <DataTable key={q+type+category+softwareTypeId} rows={rows} loading={files.isLoading} columns={[
      {key:'ten',label:kind==='package'?'Tên gói':'File',render:f=><><strong>{f.ten||f.tenFile}</strong>{f.ten&&<small>{f.tenFile||f.tenFileGoc}</small>}</>},
      ...(kind==='package'?[{key:'loai',label:'Loại',render:f=><Badge>{f.loai==='config'?'Cấu hình':f.kieuTest==='manual'?'Manual · Excel':'Tự động'}</Badge>},{key:'soTestCase',label:'Nội dung',render:f=>f.loai==='config'?'—':`${f.soTestCase} ${f.kieuTest==='manual'?'file Excel':'testcase'}`}]:kind==='shared'?[{key:'tenLoai',label:'Mục'}]:[]),
      ...(kind==='shared'&&(category==='phien-ban'||!category)?[{key:'softwareType',label:'Type phần mềm',render:f=>f.loai==='phien-ban'?f.softwareType||'Chưa phân loại':'—'}]:[]),
      {key:'kichThuoc',label:'Dung lượng',render:f=>bytes(f.kichThuoc)},...(kind!=='package'?[{key:'moTa',label:'Mô tả'}]:[]),...(kind!=='private'?[{key:'nguoiTaiLen',label:'Người tải lên'}]:[]),{key:'taiLenLuc',label:'Tải lên lúc',render:f=>date(f.taiLenLuc)},
      {key:'actions',label:'Thao tác',render:f=><div className="actions"><DownloadButton file={f} kind={kind}/><Button onClick={()=>setSelected(f)}>{kind!=='package'&&can(config.upload)?'Thông tin / sửa':'Thông tin'}</Button>{kind==='package'&&f.kieuTest!=='manual'&&can(f.loai==='config'?'CONFIG.DEPLOY':'TESTCASE.DEPLOY')&&<Button onClick={()=>setDeploying(f)}>Triển khai</Button>}{can(config.delete)&&<Button variant="danger" onClick={()=>deleteFile(f)}>Xoá</Button>}</div>},
    ]}/>{software&&<Notice>Chọn phiên bản / flash theo Request cần API quản lý phiên bản của BE. Kho này hiện lưu file và mô tả phiên bản thực tế.</Notice>}
    {uploading&&<UploadDialog kind={kind} category={category} categories={categories.data||[]} onlyTestcase={onlyTestcase} onClose={()=>setUploading(false)}/>} {selected&&<FileDetailModal file={selected} kind={kind} categories={categories.data||[]} onClose={()=>setSelected(null)}/>}
    <Modal open={!!deploying} onClose={()=>setDeploying(null)} title={`Triển khai ${deploying?.ten||''}`}><MutationForm onClose={()=>setDeploying(null)} onSave={async form=>{const result=await post(`/devices/${encodeURIComponent(form.get('device'))}/deploy`,{goiId:deploying.id});notify(`Đã gửi lệnh ${result.cmdId}. Đang chờ client xác nhận.`);await cache.invalidateQueries()}} label="Gửi tới client"><Field label="Thiết bị nhận"><select name="device" required><option value="">Chọn Bench / Vehicle / ECU</option>{(devices.data||[]).filter(d=>d.hoTroRemote).map(d=><option key={d.code} value={d.code}>{d.ten||d.code} · {d.code}</option>)}</select></Field></MutationForm></Modal>
  </>
}
export function CategoriesPage(){
  const cache=useQueryClient(),navigate=useNavigate(),categories=useApi('/du-lieu-chung/muc','DULIEU.VIEW')
  const [editing,setEditing]=useState(null),[q,setQ]=useState(''),[error,setError]=useState('')
  return <><PageHeading title="Mục dữ liệu chung" description="Thêm mục lưu file; không thể xoá mục mặc định hoặc mục còn file."><Button onClick={()=>navigate('/files')}>← Dữ liệu chung</Button></PageHeading><Toolbar value={q} onSearch={setQ} actions={<Button variant="primary" onClick={()=>setEditing({})}><Plus size={14}/>Thêm mục</Button>}/><ErrorMessage>{error||categories.error&&errorText(categories.error)}</ErrorMessage><DataTable rows={(categories.data||[]).filter(c=>matches(c,q,['ma','ten','moTa']))} columns={[{key:'ten',label:'Tên mục'},{key:'moTa',label:'Mô tả'},{key:'soFile',label:'Số file'},{key:'actions',label:'Thao tác',render:c=><div className="actions"><Button onClick={()=>setEditing(c)}>Sửa</Button><Button variant="danger" disabled={c.macDinh||c.soFile>0} onClick={async()=>{if(!window.confirm(`Xoá mục ${c.ten}?`))return;try{await remove(`/du-lieu-chung/muc/${encodeURIComponent(c.ma)}`);await cache.invalidateQueries();notify('Đã xoá mục.')}catch(e){setError(errorText(e))}}}>Xoá</Button></div>}]}/><Modal open={!!editing} onClose={()=>setEditing(null)} title={editing?.ma?'Sửa mục':'Thêm mục'}>{editing&&<MutationForm onClose={()=>setEditing(null)} onSave={async form=>{const body=Object.fromEntries(form);if(editing.ma)await patch(`/du-lieu-chung/muc/${encodeURIComponent(editing.ma)}`,body);else await post('/du-lieu-chung/muc',body);await cache.invalidateQueries();notify('Đã lưu mục.')}}><Field label="Tên mục *"><input name="ten" required defaultValue={editing.ten||''} maxLength={128}/></Field><Field label="Mô tả"><textarea name="moTa" defaultValue={editing.moTa||''} rows={3}/></Field></MutationForm>}</Modal></>
}
