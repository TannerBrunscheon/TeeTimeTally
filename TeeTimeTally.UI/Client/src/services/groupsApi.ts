import { useHttpClient } from '@/composables/useHttpClient';
import type {
  Group,
  GroupMember,
  CreateGroupRequest,
  AddGolfersToGroupRequest,
  AddGolfersToGroupResponse,
  RemoveGolfersFromGroupRequest,
  RemoveGolfersFromGroupResponse,
  SetGroupMemberScorerStatusRequest,
  SetGroupMemberScorerStatusResponse,
  GroupYearEndReportResponse,
} from '@/models';

export async function getAllGroups(): Promise<Group[]> {
  const { data } = await useHttpClient().get<Group[]>('/api/groups');
  return data;
}

export async function getGroupById(groupId: string): Promise<Group> {
  const { data } = await useHttpClient().get<Group>(`/api/groups/${groupId}`);
  return data;
}

export async function createGroup(payload: CreateGroupRequest): Promise<Group> {
  const { data } = await useHttpClient().post<Group>('/api/groups', payload);
  return data;
}

export async function updateGroup(groupId: string, payload: any): Promise<Group> {
  const { data } = await useHttpClient().put<Group>(`/api/groups/${groupId}`, payload);
  return data;
}

export async function deleteGroup(groupId: string): Promise<void> {
  await useHttpClient().delete(`/api/groups/${groupId}`);
}

export async function getGroupMembers(groupId: string): Promise<GroupMember[]> {
  const { data } = await useHttpClient().get<GroupMember[]>(`/api/groups/${groupId}/members`);
  return data;
}

export async function addGolfersToGroup(groupId: string, golferIds: string[]): Promise<AddGolfersToGroupResponse> {
  const payload: AddGolfersToGroupRequest = { golferIds };
  const { data } = await useHttpClient().post<AddGolfersToGroupResponse>(`/api/groups/${groupId}/members`, payload);
  return data;
}

export async function removeGolfersFromGroup(groupId: string, golferIds: string[]): Promise<RemoveGolfersFromGroupResponse> {
  const payload: RemoveGolfersFromGroupRequest = { golferIds };
  const { data } = await useHttpClient().delete<RemoveGolfersFromGroupResponse>(`/api/groups/${groupId}/members`, { data: payload });
  return data;
}

export async function setGroupMemberScorerStatus(groupId: string, memberGolferId: string, isScorer: boolean): Promise<SetGroupMemberScorerStatusResponse> {
  const payload: SetGroupMemberScorerStatusRequest = { isScorer };
  const { data } = await useHttpClient().put<SetGroupMemberScorerStatusResponse>(`/api/groups/${groupId}/members/${memberGolferId}/scorer-status`, payload);
  return data;
}

export async function fetchGroupYearEndReport(groupId: string, year: number): Promise<GroupYearEndReportResponse> {
  const { data } = await useHttpClient().get<GroupYearEndReportResponse>(`/api/groups/${groupId}/reports/year/${year}`);
  return data;
}
