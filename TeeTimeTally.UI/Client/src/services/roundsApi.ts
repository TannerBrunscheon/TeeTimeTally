import { useHttpClient } from '@/composables/useHttpClient';
import type {
  OpenRound,
  StartRoundRequest,
  StartRoundResponse,
  GetRoundByIdResponse,
  SubmitScoresRequest,
  SubmitScoresResponse,
  CompleteRoundRequest,
  CompleteRoundResponse,
  GetGroupRoundHistoryResponse
} from '@/models/round';

export async function fetchOpenRounds(): Promise<OpenRound[]> {
  const { data } = await useHttpClient().get<OpenRound[]>('/api/rounds/open');
  return data;
}

export async function fetchGroupRoundHistory(groupId: string): Promise<GetGroupRoundHistoryResponse> {
  const { data } = await useHttpClient().get<GetGroupRoundHistoryResponse>(`/api/groups/${groupId}/rounds/history`);
  return data;
}

export async function startRound(groupId: string, request: StartRoundRequest): Promise<StartRoundResponse> {
  const { data } = await useHttpClient().post<StartRoundResponse>(`/api/groups/${groupId}/rounds`, request);
  return data;
}

export async function fetchRoundById(roundId: string): Promise<GetRoundByIdResponse> {
  const { data } = await useHttpClient().get<GetRoundByIdResponse>(`/api/rounds/${roundId}`);
  return data;
}

export async function submitScores(roundId: string, scores: SubmitScoresRequest['scoresToSubmit']): Promise<SubmitScoresResponse> {
  const { data } = await useHttpClient().post<SubmitScoresResponse>(`/api/rounds/${roundId}/scores`, { scoresToSubmit: scores });
  return data;
}

export async function completeRound(roundId: string, request: Omit<CompleteRoundRequest, 'roundId'>): Promise<CompleteRoundResponse> {
  const { data } = await useHttpClient().post<CompleteRoundResponse>(`/api/rounds/${roundId}/complete`, request);
  return data;
}
