-- Fix materialized view definitions introduced in V2: use correct join column round_team_id
-- This migration drops and recreates the two materialized views with the proper joins.
BEGIN;

-- Drop views if they exist (safe to run repeatedly)
DROP MATERIALIZED VIEW IF EXISTS mv_round_player_diffs CASCADE;
DROP MATERIALIZED VIEW IF EXISTS mv_round_team_diffs CASCADE;

-- Recreate per-round per-player diffs materialized view
CREATE MATERIALIZED VIEW mv_round_player_diffs AS
SELECT
  rs.round_id,
  r.group_id,
  rp.golfer_id,
  SUM(rs.score - ch.par) AS player_round_diff,
  date_part('year', r.round_date)::int AS round_year
FROM round_scores rs
JOIN round_teams rt ON rs.round_team_id = rt.id
JOIN rounds r ON r.id = rs.round_id
JOIN round_participants rp ON rp.round_id = rs.round_id AND rp.round_team_id = rt.id
JOIN course_holes ch ON ch.course_id = rt.course_id AND ch.hole_number = rs.hole_number
GROUP BY rs.round_id, r.group_id, rp.golfer_id, date_part('year', r.round_date);

CREATE INDEX idx_mv_round_player_diffs_round_year ON mv_round_player_diffs (round_year);
CREATE INDEX idx_mv_round_player_diffs_group_id ON mv_round_player_diffs (group_id);

-- Recreate per-round per-team diffs materialized view
CREATE MATERIALIZED VIEW mv_round_team_diffs AS
SELECT
  rs.round_id,
  r.group_id,
  rs.round_team_id,
  SUM(rs.score - ch.par) AS team_round_diff,
  date_part('year', r.round_date)::int AS round_year
FROM round_scores rs
JOIN round_teams rt ON rs.round_team_id = rt.id
JOIN rounds r ON r.id = rs.round_id
JOIN course_holes ch ON ch.course_id = rt.course_id AND ch.hole_number = rs.hole_number
GROUP BY rs.round_id, r.group_id, rs.round_team_id, date_part('year', r.round_date);

CREATE INDEX idx_mv_round_team_diffs_round_year ON mv_round_team_diffs (round_year);
CREATE INDEX idx_mv_round_team_diffs_group_id ON mv_round_team_diffs (group_id);

COMMIT;
