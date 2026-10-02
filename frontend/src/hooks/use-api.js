import { useQuery } from '@tanstack/react-query'
import { useAuth } from '../context/auth-context'
import { get } from '../services/api'
export function useApi(path, permission, params, options = {}) {
  const { user, can } = useAuth()
  return useQuery({queryKey:[user?.id,path,params||null],queryFn:({signal})=>get(path,params,signal),enabled:!!user&&(!permission||can(permission)),...options})
}
