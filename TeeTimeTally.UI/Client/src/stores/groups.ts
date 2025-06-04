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
  // GroupFinancialConfiguration, // This is a response DTO, not needed for createGroup input directly
  GroupMember,
  AddGolfersToGroupRequest,
  AddGolfersToGroupResponse,
  RemoveGolfersFromGroupRequest,
  RemoveGolfersFromGroupResponse,
  SetGroupMemberScorerStatusRequest,
  SetGroupMemberScorerStatusResponse,
  CreateGroupRequest, // Import the request DTO
  CreateGroupFinancialConfigurationInputDTO // Import for type safety if used directly, though part of CreateGroupRequest
} from '@/models'; // Updated import path

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
      const { data } = await useHttpClient().get<Group[]>('/api/groups');
      groups.value = data;
      isLoadingGroups.value = false;
      return Result.successWithValue(data);
    } catch (error: any) {
      isLoadingGroups.value = false;
      const apiError = error as ResponseError;
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
      const apiError = error as ResponseError;
      if (apiError.response?.status === 404) {
        groupDetailError.value = AppError.notFound('Group not found.');
      } else if (apiError.response?.status === 403) {
        groupDetailError.value = AppError.failure('You do not have permission to view this group.');
      } else {
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
      const { data } = await useHttpClient().post<Group>('/api/groups', payload); // Assuming the response is a Group object
      isUpdatingGroup.value = false;
      // Optionally, refresh the list of groups after creation
      // and potentially set currentGroup if navigating directly to detail
      await fetchAllGroups();
      // currentGroup.value = data; // If you want to set it as current immediately
      return Result.successWithValue(data);
    } catch (error: any) {
      isUpdatingGroup.value = false;
      const apiError = error as ResponseError;
      let errorMessage = apiError.message || 'Failed to create group.';
      let appErrorInstance: AppError;

      if (apiError.response?.status === 409) { // Conflict
        errorMessage = (apiError.response?.data as any)?.title || 'A group with this name already exists.';
        appErrorInstance = AppError.conflict(errorMessage);
      } else if (apiError.response?.status === 400 && (apiError.response?.data as any)?.errors) { // Validation
        const validationErrors = (apiError.response.data as any).errors;
        const firstErrorKey = Object.keys(validationErrors)[0];
        const firstErrorMessage = validationErrors[firstErrorKey][0];
        errorMessage = `Validation Error: ${firstErrorMessage}`;
        appErrorInstance = AppError.validation(errorMessage);
      } else { // General failure
        appErrorInstance = AppError.failure(errorMessage);
      }

      // Set a general error for the creation process, not groupDetailError unless relevant
      groupsError.value = appErrorInstance;
      console.error('Error creating group:', appErrorInstance);
      return Result.failureWithValue<Group>(appErrorInstance);
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
      const apiError = error as ResponseError;
      let errorMessage = apiError.message || 'Failed to update group.';
      let appErrorInstance: AppError;

      if (apiError.response?.status === 409) {
        errorMessage = (apiError.response?.data as any)?.title || 'A group with this name already exists.';
        appErrorInstance = AppError.conflict(errorMessage);
      } else if (apiError.response?.status === 400 && (apiError.response?.data as any)?.errors) {
        const validationErrors = (apiError.response.data as any).errors;
        const firstErrorKey = Object.keys(validationErrors)[0];
        const firstErrorMessage = validationErrors[firstErrorKey][0];
        errorMessage = `Validation Error: ${firstErrorMessage}`;
        appErrorInstance = AppError.validation(errorMessage);
      } else if (apiError.response?.status === 404) {
        errorMessage = 'Group not found.';
        appErrorInstance = AppError.notFound(errorMessage);
      } else if (apiError.response?.status === 403) {
        errorMessage = 'You do not have permission to update this group.';
        appErrorInstance = AppError.failure(errorMessage);
      } else {
        appErrorInstance = AppError.failure(errorMessage);
      }
      groupDetailError.value = appErrorInstance;
      console.error('Error updating group:', appErrorInstance);
      return Result.failureWithValue<Group>(appErrorInstance);
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
      await useHttpClient().delete(`/api/groups/${groupId}`);
      isUpdatingGroup.value = false;
      await fetchAllGroups(); // Refresh the list
      if (currentGroup.value?.id === groupId) { // Clear currentGroup if it was the one deleted
        currentGroup.value = null;
      }
      return Result.success();
    } catch (error: any) {
      isUpdatingGroup.value = false;
      const apiError = error as ResponseError;
      let errorMessage = apiError.message || 'Failed to delete group.';
      let appErrorInstance: AppError;
      if (apiError.response?.status === 404) {
        errorMessage = 'Group not found.';
        appErrorInstance = AppError.notFound(errorMessage);
      } else if (apiError.response?.status === 403) {
        errorMessage = 'You do not have permission to delete this group.';
        appErrorInstance = AppError.failure(errorMessage);
      } else {
        appErrorInstance = AppError.failure(errorMessage);
      }
      groupsError.value = appErrorInstance;
      console.error('Error deleting group:', appErrorInstance);
      return Result.failure(appErrorInstance);
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
    // groupDetailError.value = null; // Don't clear general group detail error if only members fail
    try {
      const { data } = await useHttpClient().get<GroupMember[]>(`/api/groups/${groupId}/members`);
      isLoadingGroupDetail.value = false;
      return Result.successWithValue(data);
    } catch (error: any) {
      isLoadingGroupDetail.value = false;
      const apiError = error as ResponseError;
      let errorMessage = apiError.message || 'Failed to fetch group members.';
      let appErrorInstance: AppError;
      if (apiError.response?.status === 404) {
        errorMessage = 'Group not found when fetching members.'; // More specific
        appErrorInstance = AppError.notFound(errorMessage);
      } else if (apiError.response?.status === 403) {
        errorMessage = 'You do not have permission to view members of this group.';
        appErrorInstance = AppError.failure(errorMessage);
      } else {
        appErrorInstance = AppError.failure(errorMessage);
      }
      // Set a more specific error or add to existing groupDetailError if appropriate
      // For now, logging it but not overriding groupDetailError unless critical
      console.error('Error fetching group members:', appErrorInstance);
      return Result.failureWithValue<GroupMember[]>(appErrorInstance);
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
    // groupDetailError.value = null; // Don't clear general group error
    try {
      const payload: AddGolfersToGroupRequest = { golferIds };
      const { data } = await useHttpClient().post<AddGolfersToGroupResponse>(`/api/groups/${groupId}/members`, payload);
      isManagingMembers.value = false;
      // No need to call fetchGroupMembers here if the parent view will do it based on an emitted event
      return Result.successWithValue(data);
    } catch (error: any) {
      isManagingMembers.value = false;
      const apiError = error as ResponseError;
      let errorMessage = apiError.message || 'Failed to add golfers to group.';
      let appErrorInstance: AppError;
      if (apiError.response?.status === 400 && (apiError.response?.data as any)?.errors) {
        const validationErrors = (apiError.response.data as any).errors;
        const firstErrorKey = Object.keys(validationErrors)[0];
        const firstErrorMessage = validationErrors[firstErrorKey][0];
        errorMessage = `Validation Error: ${firstErrorMessage}`;
        appErrorInstance = AppError.validation(errorMessage);
      } else if (apiError.response?.status === 403) {
        errorMessage = 'You do not have permission to add members to this group.';
        appErrorInstance = AppError.failure(errorMessage);
      } else {
        appErrorInstance = AppError.failure(errorMessage);
      }
      // groupDetailError.value = appErrorInstance; // Or a specific member management error
      console.error('Error adding golfers to group:', appErrorInstance);
      return Result.failureWithValue<AddGolfersToGroupResponse>(appErrorInstance);
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
    // groupDetailError.value = null;
    try {
      const payload: RemoveGolfersFromGroupRequest = { golferIds };
      // For DELETE with body, Axios expects it in the `data` property of the config object
      const { data } = await useHttpClient().delete<RemoveGolfersFromGroupResponse>(`/api/groups/${groupId}/members`, { data: payload });
      isManagingMembers.value = false;
      // No need to call fetchGroupMembers here if the parent view will do it
      return Result.successWithValue(data);
    } catch (error: any) {
      isManagingMembers.value = false;
      const apiError = error as ResponseError;
      let errorMessage = apiError.message || 'Failed to remove golfers from group.';
      let appErrorInstance: AppError;
      if (apiError.response?.status === 403) {
        errorMessage = 'You do not have permission to remove members from this group.';
        appErrorInstance = AppError.failure(errorMessage);
      } else {
        appErrorInstance = AppError.failure(errorMessage);
      }
      // groupDetailError.value = appErrorInstance;
      console.error('Error removing golfers from group:', appErrorInstance);
      return Result.failureWithValue<RemoveGolfersFromGroupResponse>(appErrorInstance);
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

    isManagingMembers.value = true; // Re-use for this operation
    // groupDetailError.value = null;
    try {
      const payload: SetGroupMemberScorerStatusRequest = { isScorer };
      const { data } = await useHttpClient().put<SetGroupMemberScorerStatusResponse>(`/api/groups/${groupId}/members/${memberGolferId}/scorer-status`, payload);
      isManagingMembers.value = false;
      // No need to call fetchGroupMembers here if the parent view will do it
      return Result.successWithValue(data);
    } catch (error: any) {
      isManagingMembers.value = false;
      const apiError = error as ResponseError;
      let errorMessage = apiError.message || 'Failed to update scorer status.';
      let appErrorInstance: AppError;
      if (apiError.response?.status === 403) {
        errorMessage = 'You do not have permission to change scorer status for this group.';
        appErrorInstance = AppError.failure(errorMessage);
      } else if (apiError.response?.status === 404) {
        errorMessage = 'Group or member not found.';
        appErrorInstance = AppError.notFound(errorMessage);
      } else {
        appErrorInstance = AppError.failure(errorMessage);
      }
      // groupDetailError.value = appErrorInstance;
      console.error('Error setting scorer status:', appErrorInstance);
      return Result.failureWithValue<SetGroupMemberScorerStatusResponse>(appErrorInstance);
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
