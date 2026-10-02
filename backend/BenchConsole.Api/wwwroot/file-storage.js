// Kho cá nhân, dữ liệu chung, gói và báo cáo dùng chung luồng upload/download.
const filePages = {}, fileRows = {}, uploadJobs = new Set();
let filePreviewUrl = null;
let fileDialogGeneration = 0;
function closeFileDialog(){
  fileDialogGeneration++;
  const dialog = document.getElementById('file-dialog');
  if (dialog.open) dialog.close();
  dialog.replaceChildren();
  if (filePreviewUrl) URL.revokeObjectURL(filePreviewUrl);
  filePreviewUrl = null;
}
function renderKho(rows){if(document.activeElement?.closest?.('#kho-form'))return;storagePage('kho',rows,document.getElementById('kho-area'),drawKho);}
function renderGoi(rows){if(document.activeElement?.closest?.('#goi-form,#goi-area'))return;storagePage('goi',rows,document.getElementById('goi-area'),drawGoi);}
function renderBaoCao(rows){storagePage('bc',rows,document.getElementById('bc-area'),drawBaoCao);}
function renderDuLieuChung(rows){if(document.activeElement?.closest?.('#dlc-form'))return;storagePage('dlc',rows,document.getElementById('dlc-area'),drawDuLieuChung);}
function storagePage(key, rows, area, draw){
  fileRows[key] = rows;
  const q = (document.getElementById(key+'-q')?.value || '').trim().toLowerCase();
  const filtered = rows.filter(r => [r.ten,r.tenFile,r.tenFileGoc,r.moTa,r.benchCode,r.testCase].some(v => String(v||'').toLowerCase().includes(q)));
  const total = Math.max(1,Math.ceil(filtered.length/20));
  const page = filePages[key] = Math.min(filePages[key]||1,total);
  area.replaceChildren();
  draw(filtered.slice((page-1)*20,page*20));
  const change = n => {filePages[key]=n;storageRenderers[key](fileRows[key]);};
  const prev = el('button',{type:'button',onclick:()=>change(page-1)},'‹ Trước');prev.disabled=page===1;
  const next = el('button',{type:'button',onclick:()=>change(page+1)},'Sau ›');next.disabled=page===total;
  area.appendChild(el('div',{class:'file-pages'},el('span',{},`${filtered.length} file · Trang ${page}/${total}`),prev,next));
  document.getElementById(key+'-count').textContent=filtered.length+' / '+rows.length+' file';
}
function watchFileSearch(){
  for (const key of ['kho','goi','bc']) document.getElementById(key+'-q').addEventListener('input',()=>{
    filePages[key]=1;storageRenderers[key](fileRows[key]||[]);
  });
  document.getElementById('file-dialog').addEventListener('close',()=>{
    if(filePreviewUrl)URL.revokeObjectURL(filePreviewUrl);filePreviewUrl=null;
  });
}
function uploadFiles(path, data, form, limit){
  const files=data.getAll('file');let size=0;
  if(!files.length||files.length>100)return Promise.reject(Error('Chọn từ 1 đến 100 file.'));
  for(const f of files){if(!f.size)return Promise.reject(Error(`File ${f.name} rỗng.`));size+=f.size;}
  if(size>limit)return Promise.reject(Error(`Tổng dung lượng vượt ${limit/1024/1024} MB.`));
  const controls=[...form.querySelectorAll('input,select,textarea,button')].map(n=>[n,n.disabled]);
  controls.forEach(([n])=>n.disabled=true);
  const label=el('span',{role:'status'},'Đang tải lên…'),progress=el('progress',{max:100,value:0});
  let xhr,cancelled=false;
  const cancel=()=>{cancelled=true;xhr?.abort();};uploadJobs.add(cancel);
  const panel=el('div',{class:'file-upload'},progress,label,el('button',{type:'button',onclick:cancel},'Huỷ tải'));form.appendChild(panel);
  const send=retry=>new Promise((resolve,reject)=>{
    if(cancelled){reject(Error('Đã huỷ tải file.'));return;}
    xhr=new XMLHttpRequest();xhr.open('POST',API+path);xhr.timeout=600000;
    for(const [key,value]of Object.entries(headerAuth()))xhr.setRequestHeader(key,value);
    xhr.upload.onprogress=e=>{if(e.lengthComputable){progress.value=Math.round(e.loaded/e.total*100);label.textContent=progress.value===100?'Đang lưu và kiểm tra file…':`Đã gửi ${progress.value}%`;}};
    xhr.onerror=()=>reject(Error('Không kết nối được máy chủ. File vẫn được giữ trong ô chọn để thử lại.'));
    xhr.ontimeout=()=>reject(Error('Tải file quá thời gian. Hãy thử lại.'));
    xhr.onabort=()=>reject(Error('Đã huỷ tải file.'));
    xhr.onload=async()=>{
      try{
        if(xhr.status===401){
          if(!retry&&await thuLamMoiToken()){resolve(send(true));return;}
          luuPhien(null);veManDangNhap();throw Error('Phiên đăng nhập đã hết hạn.');
        }
        let body;try{body=JSON.parse(xhr.responseText);}catch{}
        if(xhr.status<200||xhr.status>=300)throw Error(body?.error||body?.detail||(xhr.status===413?'Dung lượng vượt giới hạn máy chủ.':xhr.status===403?'Không có quyền tải lên.':`Không lưu được file (${xhr.status}).`));
        resolve(body);
      }catch(e){reject(e);}
    };xhr.send(data);
  });
  return send(false).finally(()=>{uploadJobs.delete(cancel);panel.remove();controls.forEach(([n,disabled])=>n.disabled=disabled);});
}
async function fetchFile(path,retry=false){
  const res=await fetch(API+path,{headers:headerAuth()});
  if(res.status===401){if(!retry&&await thuLamMoiToken())return fetchFile(path,true);luuPhien(null);veManDangNhap();throw Error('Phiên đăng nhập đã hết hạn.');}
  if(!res.ok){let body;try{body=await res.json();}catch{}throw Error(body?.error||(res.status===403?'Không có quyền tải file.':`Không tải được file (${res.status}).`));}
  return res.blob();
}
function fileDetails(t,kind){
  closeFileDialog();const dialog=document.getElementById('file-dialog');
  const generation=fileDialogGeneration;
  const path=kind==='shared'?`/api/du-lieu-chung/${t.id}/download`:kind==='private'?`/api/storage/files/${t.id}/download`:kind==='package'?`/api/test-cases/${t.id}/download`:`/api/runs/reports/${t.id}/download`;
  const name=t.tenFile||t.tenFileGoc,downloadName=kind==='package'?t.ten+'.zip':name;
  const editable=kind==='shared'?coQuyen('DULIEU.UPLOAD'):kind==='private'&&coQuyen('KHO.UPLOAD');
  const form=el('form',{}),error=el('div',{role:'alert',class:'alert-msg'}),preview=el('div',{});
  const input=el('input',{value:kind==='shared'?t.ten:name,required:'required',maxlength:kind==='shared'?128:260});
  const description=el('textarea',{maxlength:512,rows:3});description.value=t.moTa||'';
  const category=el('select',{});for(const m of dsMucDuLieu)category.appendChild(el('option',{value:m.ma},m.ten));category.value=t.loai;
  form.appendChild(el('h2',{},t.ten||name));
  form.appendChild(el('p',{class:'meta'},`${name} · ${kbGon(t.kichThuoc)} · ${t.nguoiTaiLen||t.nguoiDung||t.benchCode||'—'}`));
  if(editable){form.appendChild(el('label',{},kind==='shared'?'Tên hiển thị':'Tên file'));form.appendChild(input);
    if(kind==='shared'){form.appendChild(el('label',{},'Mục lưu trữ'));form.appendChild(category);}
    form.appendChild(el('label',{},'Mô tả'));form.appendChild(description);
  }else form.appendChild(el('p',{},t.moTa||'Không có mô tả.'));
  form.appendChild(el('p',{class:'meta'},'SHA-256: '+t.sha256));form.appendChild(error);form.appendChild(preview);
  const showPreview=el('button',{type:'button',onclick:async()=>{
    showPreview.disabled=true;error.textContent='';
    try{
      const ext=name.split('.').pop().toLowerCase(),image=['png','jpg','jpeg','gif','webp','bmp'].includes(ext),text=['txt','log','csv','dbc','json','xml','md','yaml','yml','ini','cfg'].includes(ext);
      if(!image&&!text)throw Error('Định dạng này cần tải xuống và mở bằng ứng dụng phù hợp.');
      if(t.kichThuoc>2*1024*1024)throw Error('Xem trước hỗ trợ file tối đa 2 MB. Hãy tải file lớn xuống.');
      const blob=await fetchFile(path);if(!dialog.open||generation!==fileDialogGeneration)return;if(blob.size>2*1024*1024)throw Error('File quá lớn để xem trước.');
      if(image){const types={jpg:'image/jpeg',jpeg:'image/jpeg',png:'image/png',gif:'image/gif',webp:'image/webp',bmp:'image/bmp'};if(filePreviewUrl)URL.revokeObjectURL(filePreviewUrl);filePreviewUrl=URL.createObjectURL(new Blob([blob],{type:types[ext]}));preview.replaceChildren(el('img',{src:filePreviewUrl,alt:name}));}
      else{const text=await blob.text();if(dialog.open&&generation===fileDialogGeneration)preview.replaceChildren(el('pre',{},text));}
    }catch(e){error.textContent=e.message;}finally{showPreview.disabled=false;}
  }},'Xem trước');
  const save=el('button',{type:'submit'},'Lưu thay đổi');
  form.appendChild(el('div',{class:'file-pages'},showPreview,nutTai(path,downloadName),editable?save:null,el('button',{type:'button',onclick:closeFileDialog},'Đóng')));
  form.addEventListener('submit',async e=>{e.preventDefault();save.disabled=true;error.textContent='';try{
    const endpoint=kind==='shared'?`/api/du-lieu-chung/${t.id}`:`/api/storage/files/${t.id}`;
    const body=kind==='shared'?{ten:input.value,loai:category.value,moTa:description.value}:{tenFile:input.value,moTa:description.value};
    await api(endpoint,{method:'PATCH',body:JSON.stringify(body)});closeFileDialog();showToast('Đã lưu thông tin file.');await load();
  }catch(e){error.textContent=e.message;}finally{save.disabled=false;}});
  dialog.appendChild(form);dialog.showModal();
}
const storageRenderers={kho:renderKho,goi:renderGoi,bc:renderBaoCao,dlc:renderDuLieuChung};
watchFileSearch();
function syncPackageMode(){document.getElementById('goi-kieu').hidden=document.getElementById('goi-loai').value==='config';}
document.getElementById('goi-loai').addEventListener('change',syncPackageMode);
khoiDong();
