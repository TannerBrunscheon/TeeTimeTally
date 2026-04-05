import { useHttpClient } from '@/composables/useHttpClient';
import type { SearchGolfersRequest, CreateGolferRequest, CreateGolferResponse } from '@/models/golfer';
import type { Golfer } from '@/models/golfer';

export async function searchGolfers(params: SearchGolfersRequest): Promise<Golfer[]> {
  const { data } = await useHttpClient().get<Golfer[]>('/api/golfers', { params });
  return data;
}

export async function createGolfer(payload: CreateGolferRequest): Promise<CreateGolferResponse> {
  const { data } = await useHttpClient().post<CreateGolferResponse>('/api/golfers', payload);
  return data;
}
