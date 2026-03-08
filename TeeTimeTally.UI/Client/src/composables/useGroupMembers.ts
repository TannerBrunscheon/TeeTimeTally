import * as groupsApi from '@/services/groupsApi';
import { mapApiErrorToAppError } from '@/services/apiError';
import { AppError } from '@/primitives/error';
import { Result } from '@/primitives/result';

export async function fetchGroupMembersApi(groupId: string) {
  try {
    const data = await groupsApi.getGroupMembers(groupId);
    return Result.successWithValue(data);
  } catch (error: any) {
    const appError = mapApiErrorToAppError(error, 'Failed to fetch group members.');
    return Result.failureWithValue(appError);
  }
}

export async function addGolfersToGroupApi(groupId: string, golferIds: string[]) {
  try {
    const data = await groupsApi.addGolfersToGroup(groupId, golferIds);
    return Result.successWithValue(data);
  } catch (error: any) {
    const appError = mapApiErrorToAppError(error, 'Failed to add golfers to group.');
    return Result.failureWithValue(appError);
  }
}

export async function removeGolfersFromGroupApi(groupId: string, golferIds: string[]) {
  try {
    const data = await groupsApi.removeGolfersFromGroup(groupId, golferIds);
    return Result.successWithValue(data);
  } catch (error: any) {
    const appError = mapApiErrorToAppError(error, 'Failed to remove golfers from group.');
    return Result.failureWithValue(appError);
  }
}

export async function setGroupMemberScorerStatusApi(groupId: string, memberGolferId: string, isScorer: boolean) {
  try {
    const data = await groupsApi.setGroupMemberScorerStatus(groupId, memberGolferId, isScorer);
    return Result.successWithValue(data);
  } catch (error: any) {
    const appError = mapApiErrorToAppError(error, 'Failed to change scorer status.');
    return Result.failureWithValue(appError);
  }
}
