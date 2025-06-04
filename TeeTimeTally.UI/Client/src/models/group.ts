// Represents the financial configuration associated with a group
export interface GroupFinancialConfiguration {
  id: string;
  groupId: string;
  buyInAmount: number; // decimal in C#, maps to number in TS
  skinValueFormula: string;
  cthPayoutFormula: string;
  isValidated: boolean;
  createdAt: string; // ISO date string
  validatedAt: string | null; // ISO date string or null
  isDeleted?: boolean; // Optional, as it might not always be returned or relevant
  deletedAt?: string | null; // Optional
}

// Represents a group member
export interface GroupMember {
  golferId: string;
  fullName: string;
  email: string;
  isScorer: boolean;
  joinedAt: string; // ISO date string
}

// Represents a single group as returned by GetAllGroupsEndpoint
export interface Group {
  id: string;
  name: string;
  defaultCourseId: string | null;
  activeFinancialConfiguration: GroupFinancialConfiguration | null;
  createdByGolferId: string;
  createdAt: string; // ISO date string
  updatedAt: string; // ISO date string
  isDeleted: boolean;
  deletedAt: string | null;
}

// Input DTO for creating the financial configuration part of a new group
export interface CreateGroupFinancialConfigurationInputDTO {
  buyInAmount: number;
  skinValueFormula: string;
  cthPayoutFormula: string;
}

// Represents the request DTO for creating a new group
export interface CreateGroupRequest {
  name: string;
  defaultCourseId?: string | null; // Optional
  optionalInitialFinancials?: CreateGroupFinancialConfigurationInputDTO | null; // Optional
}

export interface CreateGroupResponse extends Group {}
export interface UpdateGroupRequest {
  name?: string;
  defaultCourseId?: string | null;
  existingActiveFinancialConfigurationId?: string | null;
  newFinancials?: { // This matches CreateGroupFinancialConfigurationInputDTO
    buyInAmount: number;
    skinValueFormula: string;
    cthPayoutFormula: string;
  };
}

// Represents the request DTO for adding golfers to a group
export interface AddGolfersToGroupRequest {
  golferIds: string[];
}

// Represents the response DTO for adding golfers to a group
export interface AddGolfersToGroupResponse {
  message: string;
  groupId: string;
  golfersRequestedCount: number;
  golfersSuccessfullyAddedCount: number;
  golfersAlreadyMembers: string[];
}

// Represents the request DTO for removing golfers from a group
export interface RemoveGolfersFromGroupRequest {
  golferIds: string[];
}

// Represents the response DTO for removing golfers from a group
export interface RemoveGolfersFromGroupResponse {
  message: string;
  groupId: string;
  requestedToRemoveCount: number;
  successfullyRemovedCount: number;
}

// Represents the request DTO for setting a group member's scorer status
export interface SetGroupMemberScorerStatusRequest {
  isScorer: boolean;
}

// Represents the response DTO for setting a group member's scorer status
export interface SetGroupMemberScorerStatusResponse {
  golferId: string;
  fullName: string;
  email: string;
  isScorer: boolean;
  joinedAt: string;
}
