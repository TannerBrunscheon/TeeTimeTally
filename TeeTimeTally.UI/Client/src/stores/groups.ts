// src/stores/groups.ts
import { ref } from 'vue';
import { defineStore } from 'pinia';
import { useHttpClient } from '@/composables/useHttpClient';
import { AppError, ErrorType, type ResponseError } from '@/primitives/error';
import { Result, type DefaultResult } from '@/primitives/result';
import { Permissions } from '@/models/auth/permissions';
import { useAuthenticationStore } from './authentication';
import type {
  Group,
  UpdateGroupRequest,
  GroupFinancialConfiguration,
  GroupMember,
  AddGolfersToGroupRequest,
  AddGolfersToGroupResponse,
  RemoveGolfersFromGroupRequest,
  RemoveGolfersFromGroupResponse,
  SetGroupMemberScorerStatusRequest,
  SetGroupMemberScorerStatusResponse
} from '@/models/group'; // Import the new interfaces

// New DTOs for financial validation response (if not already in models/group.ts)
// Moving these to models/group.ts would be ideal if they are API response types.
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
  const isUpdatingGroup = ref(false);
  const isManagingMembers = ref(false);

  const authenticationStore = useAuthenticationStore();

  /**
   * Fetches all groups the current user has access to.
   * This includes groups where they are an admin or a scorer.
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
      const { data } = await useHttpClient().get<Group[]>('/api/groups');
      groups.value = data;
      isLoadingGroups.value = false;
      return Result.successWithValue(data);
    } catch (error: any) {
      isLoadingGroups.value = false;
      const apiError = error as ResponseError; // Assert error type
      groupsError.value = AppError.failure((apiError.response?.data as any)?.detail || apiError.message || 'Failed to fetch groups.');
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
      const { data } = await useHttpClient().get<Group>(`/api/groups/${groupId}`);
      currentGroup.value = data;
      isLoadingGroupDetail.value = false;
      return Result.successWithValue(data);
    } catch (error: any) {
      isLoadingGroupDetail.value = false;
      const apiError = error as ResponseError; // Assert error type
      if (apiError.response?.status === 404) {
        groupDetailError.value = AppError.notFound('Group not found.');
      } else if (apiError.response?.status === 403) {
        groupDetailError.value = AppError.failure('You do not have permission to view this group.');
      } else {
        // Access properties safely with optional chaining
        const detailMessage = (apiError.response?.data as any)?.detail;
        const errorMessage = apiError.message;
        groupDetailError.value = AppError.failure(detailMessage || errorMessage || 'Failed to fetch group details.');
      }
      console.error('Error fetching group details:', groupDetailError.value);
      return Result.failureWithValue<Group>(groupDetailError.value);
    }
  }

  /**
   * Creates a new group.
   * @param name The name of the new group.
   * @param defaultCourseId Optional default course ID.
   * @param optionalInitialFinancials Optional initial financial configuration.
   */
  async function createGroup(
    name: string,
    defaultCourseId: string | null,
    optionalInitialFinancials: GroupFinancialConfiguration | null
  ): Promise<Result<Group>> {
    if (!authenticationStore.isAuthenticated || !authenticationStore.hasPermission(Permissions.CreateGroups)) {
      return Result.failureWithValue<Group>(AppError.failure('You are not authorized to create groups.'));
    }

    isUpdatingGroup.value = true; // Use this for any group-modifying operation
    groupsError.value = null;
    try {
      const payload = {
        name,
        defaultCourseId: defaultCourseId === '' ? null : defaultCourseId, // Ensure empty string becomes null
        optionalInitialFinancials: optionalInitialFinancials || null,
      };
      const { data } = await useHttpClient().post<Group>('/api/groups', payload);
      isUpdatingGroup.value = false;
      // Optionally, refresh the list of groups after creation
      await fetchAllGroups();
      return Result.successWithValue(data);
    } catch (error: any) {
      isUpdatingGroup.value = false;
      const apiError = error as ResponseError; // Assert error type
      let errorMessage = apiError.message || 'Failed to create group.';
      if (apiError.response?.status === 409) {
        errorMessage = (apiError.response?.data as any)?.title || 'A group with this name already exists.';
        return Result.failureWithValue<Group>(AppError.conflict(errorMessage));
      } else if (apiError.response?.status === 400 && (apiError.response?.data as any)?.errors) {
        // Handle validation errors from FastEndpoints
        const validationErrors = (apiError.response.data as any).errors;
        const firstErrorKey = Object.keys(validationErrors)[0];
        const firstErrorMessage = validationErrors[firstErrorKey][0];
        errorMessage = `Validation Error: ${firstErrorMessage}`;
        return Result.failureWithValue<Group>(AppError.validation(errorMessage));
      }
      groupsError.value = AppError.failure(errorMessage);
      console.error('Error creating group:', groupsError.value);
      return Result.failureWithValue<Group>(groupsError.value);
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
      const { data } = await useHttpClient().put<Group>(`/api/groups/${groupId}`, updatePayload);
      currentGroup.value = data; // Update the current group in the store
      isUpdatingGroup.value = false;
      await fetchAllGroups(); // Refresh the list in case name changed
      return Result.successWithValue(data);
    } catch (error: any) {
      isUpdatingGroup.value = false;
      const apiError = error as ResponseError; // Assert error type
      let errorMessage = apiError.message || 'Failed to update group.';
      if (apiError.response?.status === 409) {
        errorMessage = (apiError.response?.data as any)?.title || 'A group with this name already exists.';
        return Result.failureWithValue<Group>(AppError.conflict(errorMessage));
      } else if (apiError.response?.status === 400 && (apiError.response?.data as any)?.errors) {
        const validationErrors = (apiError.response.data as any).errors;
        const firstErrorKey = Object.keys(validationErrors)[0];
        const firstErrorMessage = validationErrors[firstErrorKey][0];
        errorMessage = `Validation Error: ${firstErrorMessage}`;
        return Result.failureWithValue<Group>(AppError.validation(errorMessage));
      } else if (apiError.response?.status === 404) {
        errorMessage = 'Group not found.';
        return Result.failureWithValue<Group>(AppError.notFound(errorMessage));
      } else if (apiError.response?.status === 403) {
        errorMessage = 'You do not have permission to update this group.';
        return Result.failureWithValue<Group>(AppError.failure(errorMessage));
      }
      groupDetailError.value = AppError.failure(errorMessage);
      console.error('Error updating group:', groupDetailError.value);
      return Result.failureWithValue<Group>(groupDetailError.value);
    }
  }

  /**
   * Deletes a group (soft delete).
   * @param groupId The ID of the group to delete.
   */
  async function deleteGroup(groupId: string): Promise<DefaultResult> {
    if (!authenticationStore.isAuthenticated || !authenticationStore.hasPermission(Permissions.ManageAllGroups)) {
      return Result.failure(AppError.failure('You are not authorized to delete groups.')); // Changed to Result.failure
    }

    isUpdatingGroup.value = true;
    groupsError.value = null;
    try {
      await useHttpClient().delete(`/api/groups/${groupId}`);
      isUpdatingGroup.value = false;
      await fetchAllGroups(); // Refresh the list
      return Result.success();
    } catch (error: any) {
      isUpdatingGroup.value = false;
      const apiError = error as ResponseError; // Assert error type
      let errorMessage = apiError.message || 'Failed to delete group.';
      if (apiError.response?.status === 404) {
        errorMessage = 'Group not found.';
        return Result.failure(AppError.notFound(errorMessage)); // Changed to Result.failure
      } else if (apiError.response?.status === 403) {
        errorMessage = 'You do not have permission to delete this group.';
        return Result.failure(AppError.failure(errorMessage)); // Changed to Result.failure
      }
      groupsError.value = AppError.failure(errorMessage);
      console.error('Error deleting group:', groupsError.value);
      return Result.failure(groupsError.value); // Changed to Result.failure
    }
  }

  /**
   * Fetches members of a specific group.
   * @param groupId The ID of the group.
   */
  async function fetchGroupMembers(groupId: string): Promise<Result<GroupMember[]>> {
    if (!authenticationStore.isAuthenticated || !authenticationStore.hasPermission(Permissions.ReadGroups)) {
      return Result.failureWithValue<GroupMember[]>(AppError.failure('You are not authorized to view group members.'));
    }

    isLoadingGroupDetail.value = true; // Re-use for member loading
    groupDetailError.value = null;
    try {
      const { data } = await useHttpClient().get<GroupMember[]>(`/api/groups/${groupId}/members`);
      isLoadingGroupDetail.value = false;
      return Result.successWithValue(data);
    } catch (error: any) {
      isLoadingGroupDetail.value = false;
      const apiError = error as ResponseError; // Assert error type
      let errorMessage = apiError.message || 'Failed to fetch group members.';
      if (apiError.response?.status === 404) {
        errorMessage = 'Group not found.';
        return Result.failureWithValue<GroupMember[]>(AppError.notFound(errorMessage));
      } else if (apiError.response?.status === 403) {
        errorMessage = 'You do not have permission to view members of this group.';
        return Result.failureWithValue<GroupMember[]>(AppError.failure(errorMessage));
      }
      groupDetailError.value = AppError.failure(errorMessage);
      console.error('Error fetching group members:', groupDetailError.value);
      return Result.failureWithValue<GroupMember[]>(groupDetailError.value);
    }
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
    groupDetailError.value = null;
    try {
      const payload: AddGolfersToGroupRequest = { golferIds };
      const { data } = await useHttpClient().post<AddGolfersToGroupResponse>(`/api/groups/${groupId}/members`, payload);
      isManagingMembers.value = false;
      // Refresh group members after successful addition
      await fetchGroupMembers(groupId);
      return Result.successWithValue(data);
    } catch (error: any) {
      isManagingMembers.value = false;
      const apiError = error as ResponseError; // Assert error type
      let errorMessage = apiError.message || 'Failed to add golfers to group.';
      if (apiError.response?.status === 400 && (apiError.response?.data as any)?.errors) {
        const validationErrors = (apiError.response.data as any).errors;
        const firstErrorKey = Object.keys(validationErrors)[0];
        const firstErrorMessage = validationErrors[firstErrorKey][0];
        errorMessage = `Validation Error: ${firstErrorMessage}`;
        return Result.failureWithValue<AddGolfersToGroupResponse>(AppError.validation(errorMessage));
      } else if (apiError.response?.status === 403) {
        errorMessage = 'You do not have permission to add members to this group.';
        return Result.failureWithValue<AddGolfersToGroupResponse>(AppError.failure(errorMessage));
      }
      groupDetailError.value = AppError.failure(errorMessage);
      console.error('Error adding golfers to group:', groupDetailError.value);
      return Result.failureWithValue<AddGolfersToGroupResponse>(groupDetailError.value);
    }
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
    groupDetailError.value = null;
    try {
      const payload: RemoveGolfersFromGroupRequest = { golferIds };
      const { data } = await useHttpClient().delete<RemoveGolfersFromGroupResponse>(`/api/groups/${groupId}/members`, { data: payload });
      isManagingMembers.value = false;
      // Refresh group members after successful removal
      await fetchGroupMembers(groupId);
      return Result.successWithValue(data);
    } catch (error: any) {
      isManagingMembers.value = false;
      const apiError = error as ResponseError; // Assert error type
      let errorMessage = apiError.message || 'Failed to remove golfers from group.';
      if (apiError.response?.status === 403) {
        errorMessage = 'You do not have permission to remove members from this group.';
        return Result.failureWithValue<RemoveGolfersFromGroupResponse>(AppError.failure(errorMessage));
      }
      groupDetailError.value = AppError.failure(errorMessage);
      console.error('Error removing golfers from group:', groupDetailError.value);
      return Result.failureWithValue<RemoveGolfersFromGroupResponse>(groupDetailError.value);
    }
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

    isManagingMembers.value = true;
    groupDetailError.value = null;
    try {
      const payload: SetGroupMemberScorerStatusRequest = { isScorer };
      const { data } = await useHttpClient().put<SetGroupMemberScorerStatusResponse>(`/api/groups/${groupId}/members/${memberGolferId}/scorer-status`, payload);
      isManagingMembers.value = false;
      // Refresh group members to reflect the change
      await fetchGroupMembers(groupId);
      return Result.successWithValue(data);
    } catch (error: any) {
      isManagingMembers.value = false;
      const apiError = error as ResponseError; // Assert error type
      let errorMessage = apiError.message || 'Failed to update scorer status.';
      if (apiError.response?.status === 403) {
        errorMessage = 'You do not have permission to change scorer status for this group.';
        return Result.failureWithValue<SetGroupMemberScorerStatusResponse>(AppError.failure(errorMessage));
      } else if (apiError.response?.status === 404) {
        errorMessage = 'Group or member not found.';
        return Result.failureWithValue<SetGroupMemberScorerStatusResponse>(AppError.notFound(errorMessage));
      }
      groupDetailError.value = AppError.failure(errorMessage);
      console.error('Error setting scorer status:', groupDetailError.value);
      return Result.failureWithValue<SetGroupMemberScorerStatusResponse>(groupDetailError.value);
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
  };
});
