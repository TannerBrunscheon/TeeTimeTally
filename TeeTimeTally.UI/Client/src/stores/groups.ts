import { ref } from 'vue';
import { defineStore } from 'pinia';
import * as groupsApi from '@/services/groupsApi';
import { AppError } from '@/primitives/error';
import { Result, type DefaultResult } from '@/primitives/result';
import { mapApiErrorToAppError } from '@/services/apiError';
import { Permissions } from '@/models/auth/permissions';
import { useAuthenticationStore } from './authentication';
import type {
  Group,
  UpdateGroupRequest,
  // GroupFinancialConfiguration, // This is a response DTO, not needed for createGroup input directly
  GroupMember,
  AddGolfersToGroupRequest,
  AddGolfersToGroupResponse,
  RemoveGolfersFromGroupRequest,
  RemoveGolfersFromGroupResponse,
  SetGroupMemberScorerStatusRequest,
  SetGroupMemberScorerStatusResponse,
  CreateGroupRequest, // Import the request DTO
  CreateGroupFinancialConfigurationInputDTO, // Import for type safety if used directly, though part of CreateGroupRequest,
  PlayerYearStats,
  GroupYearSummary,
  GroupYearEndReportResponse
} from '@/models'; // Updated import path

// Module shape for the dynamic composable import used to delegate member management.
type GroupMembersApiModule = {
  fetchGroupMembersApi: (groupId: string) => Promise<Result<GroupMember[]>>;
  addGolfersToGroupApi: (groupId: string, golferIds: string[]) => Promise<Result<AddGolfersToGroupResponse>>;
  removeGolfersFromGroupApi: (groupId: string, golferIds: string[]) => Promise<Result<RemoveGolfersFromGroupResponse>>;
  setGroupMemberScorerStatusApi: (groupId: string, memberGolferId: string, isScorer: boolean) => Promise<Result<SetGroupMemberScorerStatusResponse>>;
};

// These validation DTOs are specific to an API endpoint response,
// ensure they are defined in your models if they come from an API.
// For now, keeping them here as per your provided store structure.
export interface FormulaValidationError {
  message: string;
  playerCount: number | null;
}

export interface ValidateFinancialConfigurationResponse {
  isValid: boolean;
  errors: FormulaValidationError[];
}

export const useGroupsStore = defineStore('groups', () => {
  const groups = ref<Group[]>([]);
  const isLoadingGroups = ref(false);
  const groupsError = ref<AppError | null>(null);
  const currentGroup = ref<Group | null>(null); // For detail view
  const isLoadingGroupDetail = ref(false);
  const groupDetailError = ref<AppError | null>(null);
  const isUpdatingGroup = ref(false); // Used for create/update/delete operations
  const isManagingMembers = ref(false);

  const authenticationStore = useAuthenticationStore();

  /**
   * Fetches all groups the current user has access to.
   */
  async function fetchAllGroups(): Promise<Result<Group[]>> {
    if (!authenticationStore.isAuthenticated || !authenticationStore.hasPermission(Permissions.ReadGroups)) {
      groups.value = [];
      const unauthorizedError = AppError.failure('You are not authorized to view groups.');
      groupsError.value = unauthorizedError;
      return Result.failureWithValue<Group[]>(unauthorizedError);
    }

    isLoadingGroups.value = true;
    groupsError.value = null;
    try {
      const data = await groupsApi.getAllGroups();
      groups.value = data;
      isLoadingGroups.value = false;
      return Result.successWithValue(data);
    } catch (error: any) {
      isLoadingGroups.value = false;
      const appError = mapApiErrorToAppError(error, 'Failed to fetch groups.');
      groupsError.value = appError;
      console.error('Error fetching groups:', groupsError.value);
      return Result.failureWithValue<Group[]>(groupsError.value);
    }
  }

  /**
   * Fetches a single group by its ID.
   * @param groupId The ID of the group to fetch.
   */
  async function fetchGroupById(groupId: string): Promise<Result<Group>> {
    if (!authenticationStore.isAuthenticated || !authenticationStore.hasPermission(Permissions.ReadGroups)) {
      const unauthorizedError = AppError.failure('You are not authorized to view this group.');
      groupDetailError.value = unauthorizedError;
      return Result.failureWithValue<Group>(unauthorizedError);
    }

    isLoadingGroupDetail.value = true;
    groupDetailError.value = null;
    try {
      const data = await groupsApi.getGroupById(groupId);
      currentGroup.value = data;
      isLoadingGroupDetail.value = false;
      return Result.successWithValue(data);
    } catch (error: any) {
      isLoadingGroupDetail.value = false;
      const appError = mapApiErrorToAppError(error, 'Failed to fetch group details.');
      groupDetailError.value = appError;
      console.error('Error fetching group details:', groupDetailError.value);
      return Result.failureWithValue<Group>(groupDetailError.value);
    }
  }

  /**
   * Creates a new group.
   * @param request The request DTO containing group creation data.
   */
  async function createGroup(request: CreateGroupRequest): Promise<Result<Group>> {
    if (!authenticationStore.isAuthenticated || !authenticationStore.hasPermission(Permissions.CreateGroups)) {
      return Result.failureWithValue<Group>(AppError.failure('You are not authorized to create groups.'));
    }

    isUpdatingGroup.value = true;
    groupsError.value = null; // Clear previous general groups errors
    groupDetailError.value = null; // Clear previous detail errors as this is a new group

    try {
      // The payload directly matches the CreateGroupRequest DTO
      const payload: CreateGroupRequest = {
        name: request.name,
        // Ensure defaultCourseId is null if empty string or undefined, otherwise use its value
        defaultCourseId: request.defaultCourseId || null,
        optionalInitialFinancials: request.optionalInitialFinancials || null,
      };
      const data = await groupsApi.createGroup(payload);
      isUpdatingGroup.value = false;
      // Optionally, refresh the list of groups after creation
      // and potentially set currentGroup if navigating directly to detail
      await fetchAllGroups();
      // currentGroup.value = data; // If you want to set it as current immediately
      return Result.successWithValue(data);
    } catch (error: any) {
      isUpdatingGroup.value = false;
      const appError = mapApiErrorToAppError(error, 'Failed to create group.');
      groupsError.value = appError;
      console.error('Error creating group:', appError);
      return Result.failureWithValue<Group>(appError);
    }
  }

  /**
   * Updates an existing group.
   * @param groupId The ID of the group to update.
   * @param updatePayload The data to update.
   */
  async function updateGroup(groupId: string, updatePayload: UpdateGroupRequest): Promise<Result<Group>> {
    if (!authenticationStore.isAuthenticated || !authenticationStore.hasPermission(Permissions.ManageGroupSettings)) {
      return Result.failureWithValue<Group>(AppError.failure('You are not authorized to update group settings.'));
    }

    isUpdatingGroup.value = true;
    groupDetailError.value = null;
    try {
      const data = await groupsApi.updateGroup(groupId, updatePayload);
      currentGroup.value = data; // Update the current group in the store
      isUpdatingGroup.value = false;
      await fetchAllGroups(); // Refresh the list in case name changed
      return Result.successWithValue(data);
    } catch (error: any) {
      isUpdatingGroup.value = false;
      const appError = mapApiErrorToAppError(error, 'Failed to update group.');
      groupDetailError.value = appError;
      console.error('Error updating group:', appError);
      return Result.failureWithValue<Group>(appError);
    }
  }

  /**
   * Deletes a group (soft delete).
   * @param groupId The ID of the group to delete.
   */
  async function deleteGroup(groupId: string): Promise<DefaultResult> {
    if (!authenticationStore.isAuthenticated || !authenticationStore.hasPermission(Permissions.ManageAllGroups)) {
      return Result.failure(AppError.failure('You are not authorized to delete groups.'));
    }

    isUpdatingGroup.value = true;
    groupsError.value = null;
    try {
      await groupsApi.deleteGroup(groupId);
      isUpdatingGroup.value = false;
      await fetchAllGroups(); // Refresh the list
      if (currentGroup.value?.id === groupId) { // Clear currentGroup if it was the one deleted
        currentGroup.value = null;
      }
      return Result.success();
    } catch (error: any) {
      isUpdatingGroup.value = false;
      const appError = mapApiErrorToAppError(error, 'Failed to delete group.');
      groupsError.value = appError;
      console.error('Error deleting group:', appError);
      return Result.failure(appError);
    }
  }

  /**
   * Fetches members of a specific group.
   * @param groupId The ID of the group.
   */
  // Member management moved to composable useGroupMembers; these are thin delegators to keep API centralized.
  async function fetchGroupMembers(groupId: string): Promise<Result<GroupMember[]>> {
    if (!authenticationStore.isAuthenticated || !authenticationStore.hasPermission(Permissions.ReadGroups)) {
      return Result.failureWithValue<GroupMember[]>(AppError.failure('You are not authorized to view group members.'));
    }
    // lazy-load inside to avoid importing composable globally in some contexts
  const { fetchGroupMembersApi } = (await import('@/composables/useGroupMembers')) as unknown as GroupMembersApiModule;
  return await fetchGroupMembersApi(groupId);
  }

  /**
   * Adds golfers to a group.
   * @param groupId The ID of the group.
   * @param golferIds The IDs of golfers to add.
   */
  async function addGolfersToGroup(groupId: string, golferIds: string[]): Promise<Result<AddGolfersToGroupResponse>> {
    if (!authenticationStore.isAuthenticated || !authenticationStore.hasPermission(Permissions.ManageGroupMembers)) {
      return Result.failureWithValue<AddGolfersToGroupResponse>(AppError.failure('You are not authorized to add members to groups.'));
    }
    if (golferIds.length === 0) {
      return Result.failureWithValue<AddGolfersToGroupResponse>(AppError.validation('No golfers selected to add.'));
    }

    isManagingMembers.value = true;
  const { addGolfersToGroupApi } = (await import('@/composables/useGroupMembers')) as unknown as GroupMembersApiModule;
  const result = await addGolfersToGroupApi(groupId, golferIds);
    isManagingMembers.value = false;
    return result;
  }

  /**
   * Removes golfers from a group.
   * @param groupId The ID of the group.
   * @param golferIds The IDs of golfers to remove.
   */
  async function removeGolfersFromGroup(groupId: string, golferIds: string[]): Promise<Result<RemoveGolfersFromGroupResponse>> {
    if (!authenticationStore.isAuthenticated || !authenticationStore.hasPermission(Permissions.ManageGroupMembers)) {
      return Result.failureWithValue<RemoveGolfersFromGroupResponse>(AppError.failure('You are not authorized to remove members from groups.'));
    }
    if (golferIds.length === 0) {
      return Result.failureWithValue<RemoveGolfersFromGroupResponse>(AppError.validation('No golfers selected to remove.'));
    }

    isManagingMembers.value = true;
  const { removeGolfersFromGroupApi } = (await import('@/composables/useGroupMembers')) as unknown as GroupMembersApiModule;
  const result = await removeGolfersFromGroupApi(groupId, golferIds);
    isManagingMembers.value = false;
    return result;
  }

  /**
   * Sets the scorer status for a group member.
   * @param groupId The ID of the group.
   * @param memberGolferId The ID of the golfer whose status is being set.
   * @param isScorer The new scorer status.
   */
  async function setGroupMemberScorerStatus(groupId: string, memberGolferId: string, isScorer: boolean): Promise<Result<SetGroupMemberScorerStatusResponse>> {
    if (!authenticationStore.isAuthenticated || !authenticationStore.hasPermission(Permissions.ManageGroupScorers)) {
      return Result.failureWithValue<SetGroupMemberScorerStatusResponse>(AppError.failure('You are not authorized to change scorer status.'));
    }

    isManagingMembers.value = true; // Re-use for this operation
  const { setGroupMemberScorerStatusApi } = (await import('@/composables/useGroupMembers')) as unknown as GroupMembersApiModule;
  const result = await setGroupMemberScorerStatusApi(groupId, memberGolferId, isScorer);
    isManagingMembers.value = false;
    return result;
  }
  async function fetchGroupYearEndReport(groupId: string, year: number): Promise<Result<GroupYearEndReportResponse>> {
      if (!authenticationStore.isAuthenticated || !authenticationStore.hasPermission(Permissions.ReadGroupRounds)) {
        return Result.failureWithValue<GroupYearEndReportResponse>(AppError.failure('Not authorized'));
      }
      isLoadingGroupDetail.value = true;
      try {
          const data = await groupsApi.fetchGroupYearEndReport(groupId, year);
          isLoadingGroupDetail.value = false;
          return Result.successWithValue(data);
      } catch (error: any) {
        isLoadingGroupDetail.value = false;
        const appError = mapApiErrorToAppError(error, 'Failed to fetch report');
        return Result.failureWithValue<GroupYearEndReportResponse>(appError);
      }
    }



  return {
    groups,
    isLoadingGroups,
    groupsError,
    currentGroup,
    isLoadingGroupDetail,
    groupDetailError,
    isUpdatingGroup,
    isManagingMembers,
    fetchAllGroups,
    fetchGroupById,
    createGroup,
    updateGroup,
    deleteGroup,
    fetchGroupMembers,
    addGolfersToGroup,
    removeGolfersFromGroup,
    setGroupMemberScorerStatus,
    fetchGroupYearEndReport,
  };
});
