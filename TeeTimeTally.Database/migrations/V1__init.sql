-- Helper function to automatically update updated_at timestamps
CREATE OR REPLACE FUNCTION trigger_set_timestamp()
RETURNS TRIGGER AS $$
BEGIN
  -- Only update updated_at if it's not a soft delete operation
  -- or if other columns besides is_deleted/deleted_at are changing.
  -- For simplicity here, we'll let it update on any update.
  -- A more complex trigger could check:
  -- IF (TG_OP = 'UPDATE' AND OLD.is_deleted IS DISTINCT FROM NEW.is_deleted AND OLD.* IS NOT DISTINCT FROM NEW.* EXCEPT (OLD.is_deleted, OLD.deleted_at)) THEN
  --   RETURN NEW; -- Do not update updated_at if only soft delete fields changed
  -- END IF;
  NEW.updated_at = NOW();
  RETURN NEW;
END;
$$ LANGUAGE plpgsql;

-- Enum type for Round Status
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_type WHERE typname = 'round_status_enum') THEN
        CREATE TYPE round_status_enum AS ENUM (
            'PendingSetup',
            'SetupComplete',
            'InProgress',
            'Completed',
            'Finalized'
        );
    END IF;
END$$;

CREATE TABLE IF NOT EXISTS golfers (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    auth0_user_id TEXT, -- Unique constraint handled by partial index
    full_name TEXT NOT NULL,
    email TEXT, -- Unique constraint handled by partial index
    is_system_admin BOOLEAN NOT NULL DEFAULT FALSE,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    is_deleted BOOLEAN NOT NULL DEFAULT FALSE,
    deleted_at TIMESTAMPTZ DEFAULT NULL
);
DROP TRIGGER IF EXISTS set_golfers_updated_at ON golfers;
CREATE TRIGGER set_golfers_updated_at
BEFORE UPDATE ON golfers
FOR EACH ROW EXECUTE FUNCTION trigger_set_timestamp();

-- Partial unique indexes for active golfers
CREATE UNIQUE INDEX IF NOT EXISTS idx_golfers_unique_active_auth0_user_id ON golfers (auth0_user_id) WHERE is_deleted = FALSE;
CREATE UNIQUE INDEX IF NOT EXISTS idx_golfers_unique_active_email ON golfers (email) WHERE is_deleted = FALSE;

CREATE TABLE IF NOT EXISTS courses (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    name TEXT NOT NULL, -- Unique constraint handled by partial index
    cth_hole_number SMALLINT NOT NULL CHECK (cth_hole_number BETWEEN 1 AND 18),
    created_by_golfer_id UUID REFERENCES golfers(id) ON DELETE SET NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    is_deleted BOOLEAN NOT NULL DEFAULT FALSE,
    deleted_at TIMESTAMPTZ DEFAULT NULL
);
DROP TRIGGER IF EXISTS set_courses_updated_at ON courses;
CREATE TRIGGER set_courses_updated_at
BEFORE UPDATE ON courses
FOR EACH ROW EXECUTE FUNCTION trigger_set_timestamp();

-- Partial unique index for active courses
CREATE UNIQUE INDEX IF NOT EXISTS idx_courses_unique_active_name ON courses (name) WHERE is_deleted = FALSE;

CREATE TABLE IF NOT EXISTS group_financial_configurations (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    group_id UUID NOT NULL, -- FK added after groups table
    buy_in_amount DECIMAL(10, 2) NOT NULL CHECK (buy_in_amount > 0),
    skin_value_formula TEXT NOT NULL,
    cth_payout_formula TEXT NOT NULL,
    is_validated BOOLEAN NOT NULL DEFAULT FALSE,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    validated_at TIMESTAMPTZ,
    -- Soft delete might be less common here if these are treated as immutable versions
    -- but adding for consistency if direct "deletion" of a draft/unused config is needed.
    is_deleted BOOLEAN NOT NULL DEFAULT FALSE,
    deleted_at TIMESTAMPTZ DEFAULT NULL
    -- No updated_at trigger as these are typically immutable once created/validated
);

CREATE TABLE IF NOT EXISTS groups (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    name TEXT NOT NULL, -- Unique constraint handled by partial index
    default_course_id UUID REFERENCES courses(id) ON DELETE SET NULL, -- Assumes courses can be soft-deleted
    active_financial_configuration_id UUID REFERENCES group_financial_configurations(id) ON DELETE RESTRICT, -- Assumes GFCs can be soft-deleted
    created_by_golfer_id UUID REFERENCES golfers(id) ON DELETE SET NULL, -- Assumes golfers can be soft-deleted
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    is_deleted BOOLEAN NOT NULL DEFAULT FALSE,
    deleted_at TIMESTAMPTZ DEFAULT NULL
);
DROP TRIGGER IF EXISTS set_groups_updated_at ON groups;
CREATE TRIGGER set_groups_updated_at
BEFORE UPDATE ON groups
FOR EACH ROW EXECUTE FUNCTION trigger_set_timestamp();

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint WHERE conname = 'fk_gfc_group_id'
    ) THEN
        ALTER TABLE group_financial_configurations
        ADD CONSTRAINT fk_gfc_group_id
        FOREIGN KEY (group_id) REFERENCES groups(id) ON DELETE CASCADE;
    END IF;
END$$;
-- Note: If a group is hard-deleted, its GFCs are cascaded. If soft-deleted, GFCs remain but are tied to a soft-deleted group.

-- Partial unique index for active groups
CREATE UNIQUE INDEX IF NOT EXISTS idx_groups_unique_active_name ON groups (name) WHERE is_deleted = FALSE;


CREATE TABLE IF NOT EXISTS group_members (
    group_id UUID REFERENCES groups(id) ON DELETE CASCADE,
    golfer_id UUID REFERENCES golfers(id) ON DELETE CASCADE,
    is_scorer BOOLEAN NOT NULL DEFAULT FALSE,
    joined_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    PRIMARY KEY (group_id, golfer_id)
);
-- If a group or golfer is soft-deleted, this membership record becomes implicitly inactive.
-- Explicitly "removing" a member from a group would still be a hard DELETE on this table.

CREATE TABLE IF NOT EXISTS rounds (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    group_id UUID NOT NULL REFERENCES groups(id) ON DELETE CASCADE, -- If group hard-deleted
    course_id UUID NOT NULL REFERENCES courses(id) ON DELETE RESTRICT, -- Prevent hard-delete of course if active rounds exist
    financial_configuration_id UUID NOT NULL REFERENCES group_financial_configurations(id) ON DELETE RESTRICT,
    round_date DATE NOT NULL DEFAULT CURRENT_DATE,
    status round_status_enum NOT NULL,
    num_players SMALLINT CHECK (num_players >= 6),
    total_pot DECIMAL(10, 2) CHECK (total_pot >= 0),
    calculated_skin_value_per_hole DECIMAL(10, 2) CHECK (calculated_skin_value_per_hole >= 0),
    calculated_cth_payout DECIMAL(10, 2) CHECK (calculated_cth_payout >= 0),
    cth_winner_golfer_id UUID REFERENCES golfers(id) ON DELETE SET NULL,
    final_skin_rollover_amount DECIMAL(10, 2) DEFAULT 0.00,
    final_total_skins_payout DECIMAL(10, 2) DEFAULT 0.00,
    final_overall_winner_payout_amount DECIMAL(10, 2) DEFAULT 0.00,
    finalized_at TIMESTAMPTZ,
    created_by_golfer_id UUID REFERENCES golfers(id) ON DELETE SET NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    is_deleted BOOLEAN NOT NULL DEFAULT FALSE,
    deleted_at TIMESTAMPTZ DEFAULT NULL
);
DROP TRIGGER IF EXISTS set_rounds_updated_at ON rounds;
CREATE TRIGGER set_rounds_updated_at
BEFORE UPDATE ON rounds
FOR EACH ROW EXECUTE FUNCTION trigger_set_timestamp();

CREATE TABLE IF NOT EXISTS round_teams (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    round_id UUID NOT NULL REFERENCES rounds(id) ON DELETE CASCADE, -- If round hard-deleted
    team_name_or_number TEXT NOT NULL, -- Unique constraint handled by partial index
    is_overall_winner BOOLEAN NOT NULL DEFAULT FALSE,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    is_deleted BOOLEAN NOT NULL DEFAULT FALSE,
    deleted_at TIMESTAMPTZ DEFAULT NULL
    -- Original UNIQUE (round_id, team_name_or_number) moved to partial index
);
DROP TRIGGER IF EXISTS set_round_teams_updated_at ON round_teams;
CREATE TRIGGER set_round_teams_updated_at
BEFORE UPDATE ON round_teams
FOR EACH ROW EXECUTE FUNCTION trigger_set_timestamp();

-- Partial unique index for active teams within a round
CREATE UNIQUE INDEX IF NOT EXISTS idx_round_teams_unique_active_team ON round_teams (round_id, team_name_or_number) WHERE is_deleted = FALSE;


CREATE TABLE IF NOT EXISTS round_participants (
    round_id UUID REFERENCES rounds(id) ON DELETE CASCADE,
    golfer_id UUID REFERENCES golfers(id) ON DELETE CASCADE,
    round_team_id UUID NOT NULL REFERENCES round_teams(id) ON DELETE CASCADE,
    buy_in_paid BOOLEAN NOT NULL DEFAULT TRUE,
    PRIMARY KEY (round_id, golfer_id)
);
-- If round, golfer, or team is soft-deleted, this record becomes implicitly inactive.

CREATE TABLE IF NOT EXISTS round_scores (
    round_team_id UUID REFERENCES round_teams(id) ON DELETE CASCADE,
    hole_number SMALLINT NOT NULL CHECK (hole_number BETWEEN 1 AND 18),
    score SMALLINT NOT NULL,
    is_skin_winner BOOLEAN NOT NULL DEFAULT FALSE,
    skin_value_won DECIMAL(10, 2) DEFAULT 0.00,
    entered_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    round_id UUID NOT NULL REFERENCES rounds(id) ON DELETE CASCADE,
    PRIMARY KEY (round_team_id, hole_number)
);
-- If a round_team is soft-deleted, its scores are implicitly inactive.

CREATE TABLE IF NOT EXISTS round_payout_summary (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    round_id UUID NOT NULL REFERENCES rounds(id) ON DELETE CASCADE,
    golfer_id UUID NOT NULL REFERENCES golfers(id) ON DELETE CASCADE,
    team_id UUID NOT NULL REFERENCES round_teams(id) ON DELETE CASCADE,
    total_winnings DECIMAL(10,2) NOT NULL,
    breakdown JSONB,
    calculated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    UNIQUE (round_id, golfer_id)
);
-- If a round is soft-deleted, its summary is implicitly inactive.