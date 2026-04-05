-- Materialized views to speed up year-end reporting
-- 1) per-round per-player diffs
CREATE MATERIALIZED VIEW IF NOT EXISTS mv_round_player_diffs AS
SELECT
  rs.round_id,
  r.group_id,
  rp.golfer_id,
  SUM(rs.score) AS player_round_score_total,
  date_part('year', r.round_date)::int AS round_year
FROM round_scores rs
JOIN round_teams rt ON rs.round_team_id = rt.id
JOIN rounds r ON r.id = rs.round_id
JOIN round_participants rp ON rp.round_id = rs.round_id AND rp.round_team_id = rt.id
GROUP BY rs.round_id, r.group_id, rp.golfer_id, date_part('year', r.round_date);

CREATE INDEX IF NOT EXISTS idx_mv_round_player_diffs_round_year ON mv_round_player_diffs (round_year);
CREATE INDEX IF NOT EXISTS idx_mv_round_player_diffs_group_id ON mv_round_player_diffs (group_id);

-- 2) per-round per-team diffs
CREATE MATERIALIZED VIEW IF NOT EXISTS mv_round_team_diffs AS
SELECT
  rs.round_id,
  r.group_id,
  rs.round_team_id,
  SUM(rs.score) AS team_round_score_total,
  date_part('year', r.round_date)::int AS round_year
FROM round_scores rs
JOIN round_teams rt ON rs.round_team_id = rt.id
JOIN rounds r ON r.id = rs.round_id
GROUP BY rs.round_id, r.group_id, rs.round_team_id, date_part('year', r.round_date);

CREATE INDEX IF NOT EXISTS idx_mv_round_team_diffs_round_year ON mv_round_team_diffs (round_year);
CREATE INDEX IF NOT EXISTS idx_mv_round_team_diffs_group_id ON mv_round_team_diffs (group_id);

-- Note: These materialized views should be refreshed after rounds are finalized. Consider creating a scheduled job
-- or refreshing them in application code after finalization.
