-- Helper function to automatically update updated_at timestamps
CREATE OR REPLACE FUNCTION trigger_set_timestamp()
RETURNS TRIGGER AS $$
BEGIN
  NEW.updated_at = NOW();
  RETURN NEW;
END;
$$ LANGUAGE plpgsql;

-- Ensure the pgcrypto extension is available for gen_random_uuid() if not enabled by default
-- CREATE EXTENSION IF NOT EXISTS "pgcrypto"; -- (Often gen_random_uuid() is available without this now)

-- Enum type for Round Status
CREATE TYPE round_status_enum AS ENUM (
    'PendingSetup',
    'SetupComplete',
    'InProgress',
    'Completed',
    'Finalized'
);

-- 1. golfers
CREATE TABLE golfers (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    auth0_user_id TEXT UNIQUE,
    full_name TEXT NOT NULL,
    email TEXT UNIQUE NOT NULL,
    is_system_admin BOOLEAN DEFAULT FALSE,
    created_at TIMESTAMPTZ DEFAULT NOW(),
    updated_at TIMESTAMPTZ DEFAULT NOW()
);
CREATE TRIGGER set_golfers_updated_at
BEFORE UPDATE ON golfers
FOR EACH ROW EXECUTE FUNCTION trigger_set_timestamp();

-- 2. courses
CREATE TABLE courses (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    name TEXT NOT NULL UNIQUE,
    cth_hole_number SMALLINT NOT NULL CHECK (cth_hole_number BETWEEN 1 AND 18),
    created_by_golfer_id UUID REFERENCES golfers(id) ON DELETE SET NULL,
    created_at TIMESTAMPTZ DEFAULT NOW(),
    updated_at TIMESTAMPTZ DEFAULT NOW()
);
CREATE TRIGGER set_courses_updated_at
BEFORE UPDATE ON courses
FOR EACH ROW EXECUTE FUNCTION trigger_set_timestamp();

-- 3. group_financial_configurations
-- (Created before groups because groups will reference it)
CREATE TABLE group_financial_configurations (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    group_id UUID NOT NULL, -- FK added after groups table is created
    buy_in_amount DECIMAL(10, 2) NOT NULL CHECK (buy_in_amount > 0),
    skin_value_formula TEXT NOT NULL,
    cth_payout_formula TEXT NOT NULL,
    is_validated BOOLEAN DEFAULT FALSE,
    created_at TIMESTAMPTZ DEFAULT NOW(),
    validated_at TIMESTAMPTZ
);

-- 4. groups
CREATE TABLE groups (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    name TEXT NOT NULL UNIQUE,
    default_course_id UUID REFERENCES courses(id) ON DELETE SET NULL,
    active_financial_configuration_id UUID REFERENCES group_financial_configurations(id) ON DELETE RESTRICT,
    created_by_golfer_id UUID REFERENCES golfers(id) ON DELETE SET NULL,
    created_at TIMESTAMPTZ DEFAULT NOW(),
    updated_at TIMESTAMPTZ DEFAULT NOW()
);
CREATE TRIGGER set_groups_updated_at
BEFORE UPDATE ON groups
FOR EACH ROW EXECUTE FUNCTION trigger_set_timestamp();

-- Add the FK from group_financial_configurations to groups now that groups table exists
ALTER TABLE group_financial_configurations
ADD CONSTRAINT fk_gfc_group_id
FOREIGN KEY (group_id) REFERENCES groups(id) ON DELETE CASCADE;

-- 5. group_members
CREATE TABLE group_members (
    group_id UUID REFERENCES groups(id) ON DELETE CASCADE,
    golfer_id UUID REFERENCES golfers(id) ON DELETE CASCADE,
    is_scorer BOOLEAN DEFAULT FALSE,
    joined_at TIMESTAMPTZ DEFAULT NOW(),
    PRIMARY KEY (group_id, golfer_id)
);

-- 6. rounds
CREATE TABLE rounds (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    group_id UUID NOT NULL REFERENCES groups(id) ON DELETE CASCADE,
    course_id UUID NOT NULL REFERENCES courses(id) ON DELETE RESTRICT,
    financial_configuration_id UUID NOT NULL REFERENCES group_financial_configurations(id) ON DELETE RESTRICT,
    round_date DATE NOT NULL DEFAULT CURRENT_DATE,
    status round_status_enum NOT NULL, -- Using the ENUM type here
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
    created_at TIMESTAMPTZ DEFAULT NOW(),
    updated_at TIMESTAMPTZ DEFAULT NOW()
);
CREATE TRIGGER set_rounds_updated_at
BEFORE UPDATE ON rounds
FOR EACH ROW EXECUTE FUNCTION trigger_set_timestamp();

-- 7. round_teams
CREATE TABLE round_teams (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    round_id UUID NOT NULL REFERENCES rounds(id) ON DELETE CASCADE,
    team_name_or_number TEXT NOT NULL,
    is_overall_winner BOOLEAN DEFAULT FALSE,
    created_at TIMESTAMPTZ DEFAULT NOW(),
    updated_at TIMESTAMPTZ DEFAULT NOW(),
    UNIQUE (round_id, team_name_or_number)
);
CREATE TRIGGER set_round_teams_updated_at
BEFORE UPDATE ON round_teams
FOR EACH ROW EXECUTE FUNCTION trigger_set_timestamp();

-- 8. round_participants
CREATE TABLE round_participants (
    round_id UUID REFERENCES rounds(id) ON DELETE CASCADE,
    golfer_id UUID REFERENCES golfers(id) ON DELETE CASCADE,
    round_team_id UUID NOT NULL REFERENCES round_teams(id) ON DELETE CASCADE,
    buy_in_paid BOOLEAN DEFAULT TRUE,
    PRIMARY KEY (round_id, golfer_id)
);

-- 9. round_scores
CREATE TABLE round_scores (
    round_team_id UUID REFERENCES round_teams(id) ON DELETE CASCADE,
    hole_number SMALLINT NOT NULL CHECK (hole_number BETWEEN 1 AND 18),
    score SMALLINT NOT NULL,
    is_skin_winner BOOLEAN DEFAULT FALSE,
    skin_value_won DECIMAL(10, 2) DEFAULT 0.00,
    entered_at TIMESTAMPTZ DEFAULT NOW(),
    round_id UUID NOT NULL REFERENCES rounds(id) ON DELETE CASCADE, -- Denormalized as per your preference
    PRIMARY KEY (round_team_id, hole_number)
);

-- 10. round_payout_summary
CREATE TABLE round_payout_summary (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    round_id UUID NOT NULL REFERENCES rounds(id) ON DELETE CASCADE,
    golfer_id UUID NOT NULL REFERENCES golfers(id) ON DELETE CASCADE,
    team_id UUID NOT NULL REFERENCES round_teams(id) ON DELETE CASCADE,
    total_winnings DECIMAL(10,2) NOT NULL,
    breakdown JSONB,
    calculated_at TIMESTAMPTZ DEFAULT NOW(),
    UNIQUE (round_id, golfer_id)
);