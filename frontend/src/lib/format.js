export const date = value => value ? new Date(value).toLocaleString('vi-VN',{timeZone:'Asia/Ho_Chi_Minh'}) : '—'
export const bytes = size => size<1024?`${size} B`:size<1024**2?`${Math.round(size/1024)} KB`:`${(size/1024**2).toFixed(1)} MB`
export const typeName = {bench:'Bench',ecu:'Component / ECU',vehicle:'Vehicle'}
export const stateName = {idle:'Sẵn sàng',running:'Đang chạy',offline:'Mất kết nối',error:'Lỗi',unknown:'Chưa kết nối',maintenance:'Bảo trì'}
export const stateTone = state => ({idle:'success',running:'info',error:'danger',maintenance:'warning'})[state]||'neutral'
export const matches = (row,query,fields) => fields.some(key=>String(row[key]??'').toLowerCase().includes(query.trim().toLowerCase()))
