-- Flyway Seed Data Script for TeeTimeTally
-- Version: 3 (User Updates)
-- Description: Clears existing data and populates tables with initial seed data.
-- Includes specific admin users and a new scorer with open rounds.
-- Auth0UserIDs are set to NULL for all golfers to allow linking via EnsureGolferProfileEndpoint.
-- NOTE: This script is only run manually and is not part of the migration process.

-- Clear existing data in the correct order to respect foreign key constraints
DELETE FROM round_payout_summary;
DELETE FROM round_scores;
DELETE FROM round_participants;
DELETE FROM round_teams;
DELETE FROM rounds;
DELETE FROM group_members;
-- Optional: UPDATE groups SET active_financial_configuration_id = NULL;
DELETE FROM group_financial_configurations;
DELETE FROM groups;
DELETE FROM courses;
DELETE FROM golfers;

-- Helper function to generate random scores
CREATE OR REPLACE FUNCTION get_random_score(min_val INT, max_val INT)
RETURNS INT AS $$
BEGIN
  RETURN floor(random() * (max_val - min_val + 1) + min_val)::INT;
END;
$$ LANGUAGE plpgsql;

DO $$
DECLARE
    -- Golfers (Using valid HEX characters for the first digit)
    admin_golfer_id_1 UUID := 'A0000000-0000-0000-0000-000000000001'; -- Tanner Brunscheon
    admin_golfer_id_2 UUID := 'A0000000-0000-0000-0000-000000000002'; -- Jeff Brunscheon
    admin_golfer_id_3 UUID := 'A0000000-0000-0000-0000-000000000003'; -- Todd Kuethe
    scorer_golfer_s1 UUID := 'B0000000-0000-0000-0000-000000000001';
    scorer_golfer_s2 UUID := 'B0000000-0000-0000-0000-000000000002';
    scorer_golfer_s3 UUID := 'B0000000-0000-0000-0000-000000000003';
    scorer_golfer_s4 UUID := 'B0000000-0000-0000-0000-000000000004';
    scorer_golfer_s5 UUID := 'B0000000-0000-0000-0000-000000000005'; -- This one is marked deleted later
    scorer_golfer_s6 UUID := 'B0000000-0000-0000-0000-000000000006'; -- Secret Agent Man
    regular_golfer_r1 UUID := 'C0000000-0000-0000-0000-000000000001';
    regular_golfer_r2 UUID := 'C0000000-0000-0000-0000-000000000002';
    regular_golfer_r3 UUID := 'C0000000-0000-0000-0000-000000000003';
    regular_golfer_r4 UUID := 'C0000000-0000-0000-0000-000000000004';
    regular_golfer_r5 UUID := 'C0000000-0000-0000-0000-000000000005';
    regular_golfer_r6 UUID := 'C0000000-0000-0000-0000-000000000006';
    regular_golfer_r7 UUID := 'C0000000-0000-0000-0000-000000000007';
    regular_golfer_r8 UUID := 'C0000000-0000-0000-0000-000000000008';
    regular_golfer_r9 UUID := 'C0000000-0000-0000-0000-000000000009';
    regular_golfer_r10 UUID := 'C0000000-0000-0000-0000-000000000010';
    regular_golfer_r11 UUID := 'C0000000-0000-0000-0000-000000000011';
    regular_golfer_r12 UUID := 'C0000000-0000-0000-0000-000000000012';
    regular_golfer_r13_deleted UUID := 'C0000000-0000-0000-0000-000000000013';
    regular_golfer_r14 UUID := 'C0000000-0000-0000-0000-000000000014';
    regular_golfer_r15 UUID := 'C0000000-0000-0000-0000-000000000015';
    regular_golfer_r16 UUID := 'C0000000-0000-0000-0000-000000000016';
    regular_golfer_r17 UUID := 'C0000000-0000-0000-0000-000000000017';
    regular_golfer_r18 UUID := 'C0000000-0000-0000-0000-000000000018';
    regular_golfer_r19 UUID := 'C0000000-0000-0000-0000-000000000019';
    regular_golfer_r20 UUID := 'C0000000-0000-0000-0000-000000000020';

    -- Courses
    course_c1 UUID := 'D0000000-0000-0000-0000-000000000001';
    course_c2 UUID := 'D0000000-0000-0000-0000-000000000002';
    course_c3 UUID := 'D0000000-0000-0000-0000-000000000003';
    course_c4_deleted UUID := 'D0000000-0000-0000-0000-000000000004';

    -- Groups
    group_g1 UUID := 'E0000000-0000-0000-0000-000000000001';
    group_g2 UUID := 'E0000000-0000-0000-0000-000000000002';
    group_g3_deleted UUID := 'E0000000-0000-0000-0000-000000000003';
    group_g4_no_fin UUID := 'E0000000-0000-0000-0000-000000000004';
    group_g5_complex_fin UUID := 'E0000000-0000-0000-0000-000000000005';
    group_g6_secret_agents UUID := 'E0000000-0000-0000-0000-000000000006';


    -- Group Financial Configurations (GFCs)
    gfc_g1_active UUID := 'F0000000-0000-0000-0000-000000000001';
    gfc_g2_active UUID := 'F0000000-0000-0000-0000-000000000002';
    gfc_g5_active UUID := 'F0000000-0000-0000-0000-000000000003';
    gfc_g1_old_invalid UUID := 'F0000000-0000-0000-0000-000000000004';
    gfc_deleted UUID := 'F0000000-0000-0000-0000-000000000005';
    gfc_g3_for_deleted_group UUID := 'F0000000-0000-0000-0000-000000000006';
    gfc_g6_active UUID := 'F0000000-0000-0000-0000-000000000007';


    -- Rounds
    round_1_finalized_g1_c1 UUID := '10000000-0000-0000-0000-000000000001';
    round_2_completed_g2_c2 UUID := '20000000-0000-0000-0000-000000000002';
    round_3_inprogress_g1_c3 UUID := '30000000-0000-0000-0000-000000000003';
    round_4_setupcomplete_g5_c1 UUID := '40000000-0000-0000-0000-000000000004';
    round_5_pending_g2_c2 UUID := '50000000-0000-0000-0000-000000000005';
    round_6_finalized_rollover_g1_c1 UUID := '60000000-0000-0000-0000-000000000006';
    round_7_completed_tie_g2_c3 UUID := '70000000-0000-0000-0000-000000000007';
    round_8_pending_g6_c1 UUID := '80000000-0000-0000-0000-000000000008';
    round_9_setupcomplete_g6_c2 UUID := '90000000-0000-0000-0000-000000000009';
    round_10_inprogress_g6_c3 UUID := 'A1000000-0000-0000-0000-000000000010'; -- Using A for 10 to keep it hex

    -- Round Teams (will be generated per round using gen_random_uuid())
    rt_r1_t1 UUID; rt_r1_t2 UUID; rt_r1_t3 UUID;
    rt_r2_t1 UUID; rt_r2_t2 UUID; rt_r2_t3 UUID; rt_r2_t4 UUID;
    rt_r3_t1 UUID; rt_r3_t2 UUID; rt_r3_t3 UUID; rt_r3_t4 UUID; rt_r3_t5 UUID;
    rt_r4_t1 UUID; rt_r4_t2 UUID; rt_r4_t3 UUID;
    rt_r6_t1 UUID; rt_r6_t2 UUID; rt_r6_t3 UUID; rt_r6_t4 UUID; rt_r6_t5 UUID; rt_r6_t6 UUID;
    rt_r7_t1 UUID; rt_r7_t2 UUID; rt_r7_t3 UUID;
    rt_r9_t1 UUID; rt_r9_t2 UUID; rt_r9_t3 UUID; -- For Round 9
    rt_r10_t1 UUID; rt_r10_t2 UUID; rt_r10_t3 UUID; -- For Round 10


    hole_idx INT;

BEGIN

-- 1. golfers
RAISE NOTICE 'Seeding golfers...';
INSERT INTO golfers (id, auth0_user_id, full_name, email, is_system_admin, created_at, updated_at, is_deleted, deleted_at) VALUES
(admin_golfer_id_1, NULL, 'Tanner Brunscheon', 'tannerbrunscheon@gmail.com', TRUE, '2024-01-01 10:00:00+00', '2024-01-01 10:05:00+00', FALSE, NULL),
(admin_golfer_id_2, NULL, 'Jeff Brunscheon', 'jeffb@rtc279.com', TRUE, '2024-01-01 10:00:00+00', '2024-01-01 10:05:00+00', FALSE, NULL),
(admin_golfer_id_3, NULL, 'Todd Kuethe', 'tkuethe@rtc279.com', TRUE, '2024-01-01 10:00:00+00', '2024-01-01 10:05:00+00', FALSE, NULL),
(scorer_golfer_s1, NULL, 'Scorer Alice', 'scorer.alice@example.com', FALSE, '2024-01-01 10:00:00+00', '2024-01-01 10:05:00+00', FALSE, NULL),
(scorer_golfer_s2, NULL, 'Scorer Bob', 'scorer.bob@example.com', FALSE, '2024-01-01 10:00:00+00', '2024-01-01 10:05:00+00', FALSE, NULL),
(scorer_golfer_s3, NULL, 'Scorer Carol', 'scorer.carol@example.com', FALSE, '2024-01-01 10:00:00+00', '2024-01-01 10:05:00+00', FALSE, NULL),
(scorer_golfer_s4, NULL, 'Scorer Dave', 'scorer.dave@example.com', FALSE, '2024-01-01 10:00:00+00', '2024-01-01 10:05:00+00', FALSE, NULL),
(scorer_golfer_s5, NULL, 'Scorer Eve (Deleted)', 'scorer.eve.deleted@example.com', FALSE, '2024-01-01 10:00:00+00', '2024-01-01 10:05:00+00', TRUE, '2024-03-01 14:00:00+00'),
(scorer_golfer_s6, NULL, 'Tanner Brunscheon', 'secritagentman@gmail.com', FALSE, '2024-01-01 10:00:00+00', '2024-01-01 10:05:00+00', FALSE, NULL),
(regular_golfer_r1, NULL, 'Regular Fred', 'fred@example.com', FALSE, '2024-01-01 10:00:00+00', '2024-01-01 10:05:00+00', FALSE, NULL),
(regular_golfer_r2, NULL, 'Regular Grace', 'grace@example.com', FALSE, '2024-01-01 10:00:00+00', '2024-01-01 10:05:00+00', FALSE, NULL),
(regular_golfer_r3, NULL, 'Regular Hank', 'hank@example.com', FALSE, '2024-01-01 10:00:00+00', '2024-01-01 10:05:00+00', FALSE, NULL),
(regular_golfer_r4, NULL, 'Regular Ivy', 'ivy@example.com', FALSE, '2024-01-01 10:00:00+00', '2024-01-01 10:05:00+00', FALSE, NULL),
(regular_golfer_r5, NULL, 'Regular Jack', 'jack@example.com', FALSE, '2024-01-01 10:00:00+00', '2024-01-01 10:05:00+00', FALSE, NULL),
(regular_golfer_r6, NULL, 'Regular Kate', 'kate@example.com', FALSE, '2024-01-01 10:00:00+00', '2024-01-01 10:05:00+00', FALSE, NULL),
(regular_golfer_r7, NULL, 'Regular Leo', 'leo@example.com', FALSE, '2024-01-01 10:00:00+00', '2024-01-01 10:05:00+00', FALSE, NULL),
(regular_golfer_r8, NULL, 'Regular Mia', 'mia@example.com', FALSE, '2024-01-01 10:00:00+00', '2024-01-01 10:05:00+00', FALSE, NULL),
(regular_golfer_r9, NULL, 'Regular Noah', 'noah@example.com', FALSE, '2024-01-01 10:00:00+00', '2024-01-01 10:05:00+00', FALSE, NULL),
(regular_golfer_r10, NULL, 'Regular Olivia', 'olivia@example.com', FALSE, '2024-01-01 10:00:00+00', '2024-01-01 10:05:00+00', FALSE, NULL),
(regular_golfer_r11, NULL, 'Regular Paul', 'paul@example.com', FALSE, '2024-01-01 10:00:00+00', '2024-01-01 10:05:00+00', FALSE, NULL),
(regular_golfer_r12, NULL, 'Regular Quinn', 'quinn@example.com', FALSE, '2024-01-01 10:00:00+00', '2024-01-01 10:05:00+00', FALSE, NULL),
(regular_golfer_r13_deleted, NULL, 'Regular Rita (Deleted)', 'rita.deleted@example.com', FALSE, '2024-01-01 10:00:00+00', '2024-01-01 10:05:00+00', TRUE, '2024-03-01 14:00:00+00'),
(regular_golfer_r14, NULL, 'Regular Sam', 'sam@example.com', FALSE, '2024-01-01 10:00:00+00', '2024-01-01 10:05:00+00', FALSE, NULL),
(regular_golfer_r15, NULL, 'Regular Tina', 'tina@example.com', FALSE, '2024-01-01 10:00:00+00', '2024-01-01 10:05:00+00', FALSE, NULL),
(regular_golfer_r16, NULL, 'Regular Uma', 'uma@example.com', FALSE, '2024-01-01 10:00:00+00', '2024-01-01 10:05:00+00', FALSE, NULL),
(regular_golfer_r17, NULL, 'Regular Victor', 'victor@example.com', FALSE, '2024-01-01 10:00:00+00', '2024-01-01 10:05:00+00', FALSE, NULL),
(regular_golfer_r18, NULL, 'Regular Wendy', 'wendy@example.com', FALSE, '2024-01-01 10:00:00+00', '2024-01-01 10:05:00+00', FALSE, NULL),
(regular_golfer_r19, NULL, 'Regular Xavier', 'xavier@example.com', FALSE, '2024-01-01 10:00:00+00', '2024-01-01 10:05:00+00', FALSE, NULL),
(regular_golfer_r20, NULL, 'Regular Yara', 'yara@example.com', FALSE, '2024-01-01 10:00:00+00', '2024-01-01 10:05:00+00', FALSE, NULL);

-- 2. courses (No changes needed here based on request)
RAISE NOTICE 'Seeding courses...';
INSERT INTO courses (id, name, cth_hole_number, created_by_golfer_id, created_at, updated_at, is_deleted, deleted_at) VALUES
(course_c1, 'Pebble Peach Golf Links', 7, admin_golfer_id_1, '2024-01-01 10:00:00+00', '2024-01-01 10:05:00+00', FALSE, NULL),
(course_c2, 'Augusta National Golf Club', 16, admin_golfer_id_1, '2024-01-01 10:00:00+00', '2024-01-01 10:05:00+00', FALSE, NULL),
(course_c3, 'St. Andrews Links (Old Course)', 11, admin_golfer_id_2, '2024-01-01 10:00:00+00', '2024-01-01 10:05:00+00', FALSE, NULL),
(course_c4_deleted, 'Old Mill Golf Course', 3, admin_golfer_id_1, '2024-01-01 10:00:00+00', '2024-01-01 10:05:00+00', TRUE, '2024-03-01 14:00:00+00');

-- 3. groups
RAISE NOTICE 'Seeding groups (phase 1)...';
INSERT INTO groups (id, name, default_course_id, created_by_golfer_id, created_at, updated_at, is_deleted, deleted_at, active_financial_configuration_id) VALUES
(group_g1, 'Weekend Warriors', course_c1, scorer_golfer_s1, '2024-01-01 10:00:00+00', '2024-01-01 10:05:00+00', FALSE, NULL, NULL),
(group_g2, 'Thursday Skins Crew', course_c2, scorer_golfer_s2, '2024-01-01 10:00:00+00', '2024-01-01 10:05:00+00', FALSE, NULL, NULL),
(group_g3_deleted, 'Bogey Men', course_c1, scorer_golfer_s3, '2024-01-01 10:00:00+00', '2024-01-01 10:05:00+00', TRUE, '2024-03-01 14:00:00+00', NULL),
(group_g4_no_fin, 'Newbies Club', course_c3, scorer_golfer_s4, '2024-01-01 10:00:00+00', '2024-01-01 10:05:00+00', FALSE, NULL, NULL),
(group_g5_complex_fin, 'High Rollers League', course_c2, admin_golfer_id_2, '2024-01-01 10:00:00+00', '2024-01-01 10:05:00+00', FALSE, NULL, NULL),
(group_g6_secret_agents, 'Secret Agent''s Skins Game', course_c1, scorer_golfer_s6, '2024-01-01 10:00:00+00', '2024-01-01 10:05:00+00', FALSE, NULL, NULL);

-- 4. group_financial_configurations
RAISE NOTICE 'Seeding group_financial_configurations...';
INSERT INTO group_financial_configurations (id, group_id, buy_in_amount, skin_value_formula, cth_payout_formula, is_validated, created_at, validated_at, is_deleted, deleted_at) VALUES
(gfc_g1_active, group_g1, 20.00, '2 + ({{roundPlayers}} - 6) * 0.5', '{{roundPlayers}} * 1', TRUE, '2024-01-01 10:00:00+00', '2024-01-02 11:00:00+00', FALSE, NULL),
(gfc_g2_active, group_g2, 10.00, '1 + ({{roundPlayers}} - 6) * 0.25', '5 + ({{roundPlayers}} - 6) * 0.5', TRUE, '2024-01-01 10:00:00+00', '2024-01-02 11:00:00+00', FALSE, NULL),
(gfc_g5_active, group_g5_complex_fin, 50.00, '5 + ({{roundPlayers}} - 10) * 1', '20 + ({{roundPlayers}} - 10) * 2', TRUE, '2024-01-01 10:00:00+00', '2024-01-02 11:00:00+00', FALSE, NULL),
(gfc_g1_old_invalid, group_g1, 5.00, '{{roundPlayers}} / 2', '{{roundPlayers}} * 50', FALSE, '2023-12-01 10:00:00+00', NULL, FALSE, NULL),
(gfc_deleted, group_g1, 15.00, '1.5', '10', TRUE, '2023-11-01 10:00:00+00', '2023-11-02 11:00:00+00', TRUE, '2024-03-01 14:00:00+00'),
(gfc_g3_for_deleted_group, group_g3_deleted, 25.00, '3', '15', TRUE, '2024-01-01 10:00:00+00', '2024-01-02 11:00:00+00', FALSE, NULL),
(gfc_g6_active, group_g6_secret_agents, 15.00, '1.5 + ({{roundPlayers}} - 6) * 0.30', '10', TRUE, '2024-01-01 10:00:00+00', '2024-01-02 11:00:00+00', FALSE, NULL);

-- Update groups with active_financial_configuration_id
RAISE NOTICE 'Seeding groups (phase 2 - linking financial configs)...';
UPDATE groups SET active_financial_configuration_id = gfc_g1_active, updated_at = '2024-01-15 12:00:00+00' WHERE id = group_g1;
UPDATE groups SET active_financial_configuration_id = gfc_g2_active, updated_at = '2024-01-15 12:00:00+00' WHERE id = group_g2;
UPDATE groups SET active_financial_configuration_id = gfc_g5_active, updated_at = '2024-01-15 12:00:00+00' WHERE id = group_g5_complex_fin;
UPDATE groups SET active_financial_configuration_id = gfc_g3_for_deleted_group, updated_at = '2024-01-15 12:00:00+00' WHERE id = group_g3_deleted;
UPDATE groups SET active_financial_configuration_id = gfc_g6_active, updated_at = '2024-01-15 12:00:00+00' WHERE id = group_g6_secret_agents;


-- 5. group_members
RAISE NOTICE 'Seeding group_members...';
INSERT INTO group_members (group_id, golfer_id, is_scorer, joined_at) VALUES
(group_g1, scorer_golfer_s1, TRUE, '2024-01-01 10:00:00+00'), (group_g1, admin_golfer_id_1, TRUE, '2024-01-01 10:00:00+00'),
(group_g1, regular_golfer_r1, FALSE, '2024-01-01 10:00:00+00'), (group_g1, regular_golfer_r2, FALSE, '2024-01-01 10:00:00+00'),
(group_g1, regular_golfer_r3, FALSE, '2024-01-01 10:00:00+00'), (group_g1, regular_golfer_r4, FALSE, '2024-01-01 10:00:00+00'),
(group_g1, regular_golfer_r5, FALSE, '2024-01-01 10:00:00+00'), (group_g1, regular_golfer_r6, FALSE, '2024-01-01 10:00:00+00'),
(group_g1, regular_golfer_r14, FALSE, '2024-01-01 10:00:00+00'), (group_g1, regular_golfer_r15, FALSE, '2024-01-01 10:00:00+00'),
(group_g1, regular_golfer_r16, FALSE, '2024-01-01 10:00:00+00'), (group_g1, regular_golfer_r17, FALSE, '2024-01-01 10:00:00+00'),
(group_g1, regular_golfer_r18, FALSE, '2024-01-01 10:00:00+00'),
(group_g2, scorer_golfer_s2, TRUE, '2024-01-01 10:00:00+00'), (group_g2, scorer_golfer_s3, TRUE, '2024-01-01 10:00:00+00'),
(group_g2, regular_golfer_r7, FALSE, '2024-01-01 10:00:00+00'), (group_g2, regular_golfer_r8, FALSE, '2024-01-01 10:00:00+00'),
(group_g2, regular_golfer_r9, FALSE, '2024-01-01 10:00:00+00'), (group_g2, regular_golfer_r10, FALSE, '2024-01-01 10:00:00+00'),
(group_g2, regular_golfer_r11, FALSE, '2024-01-01 10:00:00+00'), (group_g2, regular_golfer_r12, FALSE, '2024-01-01 10:00:00+00'),
(group_g2, admin_golfer_id_2, FALSE, '2024-01-01 10:00:00+00'),
(group_g3_deleted, scorer_golfer_s3, TRUE, '2023-12-01 10:00:00+00'), (group_g3_deleted, regular_golfer_r1, FALSE, '2023-12-01 10:00:00+00'),
(group_g4_no_fin, scorer_golfer_s4, TRUE, '2024-01-01 10:00:00+00'), (group_g4_no_fin, regular_golfer_r19, FALSE, '2024-01-01 10:00:00+00'),
(group_g4_no_fin, regular_golfer_r20, FALSE, '2024-01-01 10:00:00+00'),
(group_g5_complex_fin, admin_golfer_id_2, TRUE, '2024-01-01 10:00:00+00'), (group_g5_complex_fin, scorer_golfer_s1, TRUE, '2024-01-01 10:00:00+00'),
(group_g5_complex_fin, regular_golfer_r1, FALSE, '2024-01-01 10:00:00+00'), (group_g5_complex_fin, regular_golfer_r2, FALSE, '2024-01-01 10:00:00+00'),
(group_g5_complex_fin, regular_golfer_r4, FALSE, '2024-01-01 10:00:00+00'), (group_g5_complex_fin, regular_golfer_r5, FALSE, '2024-01-01 10:00:00+00'),
(group_g5_complex_fin, regular_golfer_r6, FALSE, '2024-01-01 10:00:00+00'),
-- Group G6: Secret Agent's Skins Game
(group_g6_secret_agents, scorer_golfer_s6, TRUE, '2024-01-01 10:00:00+00'),
(group_g6_secret_agents, regular_golfer_r15, FALSE, '2024-01-01 10:00:00+00'),
(group_g6_secret_agents, regular_golfer_r16, FALSE, '2024-01-01 10:00:00+00'),
(group_g6_secret_agents, regular_golfer_r17, FALSE, '2024-01-01 10:00:00+00'),
(group_g6_secret_agents, regular_golfer_r18, FALSE, '2024-01-01 10:00:00+00'),
(group_g6_secret_agents, regular_golfer_r19, FALSE, '2024-01-01 10:00:00+00'),
(group_g6_secret_agents, regular_golfer_r20, FALSE, '2024-01-01 10:00:00+00');


-- 6. rounds
RAISE NOTICE 'Seeding rounds...';
INSERT INTO rounds (id, group_id, course_id, financial_configuration_id, round_date, status, num_players, total_pot, calculated_skin_value_per_hole, calculated_cth_payout, cth_winner_golfer_id, final_skin_rollover_amount, final_total_skins_payout, final_overall_winner_payout_amount, finalized_at, created_by_golfer_id, created_at, updated_at) VALUES
(round_1_finalized_g1_c1, group_g1, course_c1, gfc_g1_active, '2024-05-10', 'Finalized', 6, 120.00, 2.00, 6.00, regular_golfer_r1, 0.00, 36.00, 78.00, '2024-05-10 18:00:00+00', scorer_golfer_s1, '2024-05-10 08:00:00+00', '2024-05-10 18:05:00+00'),
(round_2_completed_g2_c2, group_g2, course_c2, gfc_g2_active, '2024-05-15', 'Completed', 8, 80.00, 1.50, 7.00, regular_golfer_r8, scorer_golfer_s2, '2024-05-15 09:00:00+00', '2024-05-15 17:00:00+00'),
(round_3_inprogress_g1_c3, group_g1, course_c3, gfc_g1_active, '2024-05-20', 'InProgress', 10, 200.00, 4.00, 10.00, scorer_golfer_s1, '2024-05-20 07:00:00+00', '2024-05-20 14:00:00+00'),
(round_4_setupcomplete_g5_c1, group_g5_complex_fin, course_c1, gfc_g5_active, '2024-05-25', 'SetupComplete', 7, 350.00, 5.00, 20.00, admin_golfer_id_2, '2024-05-25 10:00:00+00', '2024-05-25 10:05:00+00'),
(round_5_pending_g2_c2, group_g2, course_c2, gfc_g2_active, '2024-06-01', 'PendingSetup', scorer_golfer_s2, '2024-05-28 11:00:00+00', '2024-05-28 11:00:00+00'),
(round_6_finalized_rollover_g1_c1, group_g1, course_c1, gfc_g1_active, '2024-04-20', 'Finalized', 12, 240.00, 5.00, 12.00, regular_golfer_r14, 10.00, 80.00, 138.00, '2024-04-20 19:00:00+00', scorer_golfer_s1, '2024-04-20 08:30:00+00', '2024-04-20 19:05:00+00'),
(round_7_completed_tie_g2_c3, group_g2, course_c3, gfc_g2_active, '2024-04-25', 'Completed', 6, 60.00, 1.00, 5.50, regular_golfer_r9, scorer_golfer_s3, '2024-04-25 09:00:00+00', '2024-04-25 17:30:00+00'),
-- New rounds for Secret Agent Man
(round_8_pending_g6_c1, group_g6_secret_agents, course_c1, gfc_g6_active, '2024-06-05', 'PendingSetup', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, scorer_golfer_s6, '2024-06-01 10:00:00+00', '2024-06-01 10:00:00+00'),
(round_9_setupcomplete_g6_c2, group_g6_secret_agents, course_c2, gfc_g6_active, '2024-06-10', 'SetupComplete', 6, 90.00, 1.50, 10.00, NULL, NULL, NULL, NULL, NULL, scorer_golfer_s6, '2024-06-02 10:00:00+00', '2024-06-02 10:05:00+00'),
(round_10_inprogress_g6_c3, group_g6_secret_agents, course_c3, gfc_g6_active, '2024-06-15', 'InProgress', 6, 90.00, 1.50, 10.00, NULL, NULL, NULL, NULL, NULL, scorer_golfer_s6, '2024-06-03 10:00:00+00', '2024-06-03 11:00:00+00');


-- 7. round_teams
RAISE NOTICE 'Seeding round_teams...';
rt_r1_t1 := gen_random_uuid(); rt_r1_t2 := gen_random_uuid(); rt_r1_t3 := gen_random_uuid();
INSERT INTO round_teams (id, round_id, team_name_or_number, is_overall_winner, created_at, updated_at) VALUES
(rt_r1_t1, round_1_finalized_g1_c1, 'Team Alpha', TRUE, '2024-05-10 08:00:00+00', '2024-05-10 18:00:00+00'),
(rt_r1_t2, round_1_finalized_g1_c1, 'Team Bravo', FALSE, '2024-05-10 08:00:00+00', '2024-05-10 18:00:00+00'),
(rt_r1_t3, round_1_finalized_g1_c1, 'Team Charlie', FALSE, '2024-05-10 08:00:00+00', '2024-05-10 18:00:00+00');
rt_r2_t1 := gen_random_uuid(); rt_r2_t2 := gen_random_uuid(); rt_r2_t3 := gen_random_uuid(); rt_r2_t4 := gen_random_uuid();
INSERT INTO round_teams (id, round_id, team_name_or_number, is_overall_winner, created_at, updated_at) VALUES
(rt_r2_t1, round_2_completed_g2_c2, 'Eagles', FALSE, '2024-05-15 09:00:00+00', '2024-05-15 09:00:00+00'),
(rt_r2_t2, round_2_completed_g2_c2, 'Birdies', FALSE, '2024-05-15 09:00:00+00', '2024-05-15 09:00:00+00'),
(rt_r2_t3, round_2_completed_g2_c2, 'Pars', FALSE, '2024-05-15 09:00:00+00', '2024-05-15 09:00:00+00'),
(rt_r2_t4, round_2_completed_g2_c2, 'Bogeys', FALSE, '2024-05-15 09:00:00+00', '2024-05-15 09:00:00+00');
rt_r3_t1 := gen_random_uuid(); rt_r3_t2 := gen_random_uuid(); rt_r3_t3 := gen_random_uuid(); rt_r3_t4 := gen_random_uuid(); rt_r3_t5 := gen_random_uuid();
INSERT INTO round_teams (id, round_id, team_name_or_number, created_at, updated_at) VALUES
(rt_r3_t1, round_3_inprogress_g1_c3, 'Team 1', '2024-05-20 07:00:00+00', '2024-05-20 07:00:00+00'),
(rt_r3_t2, round_3_inprogress_g1_c3, 'Team 2', '2024-05-20 07:00:00+00', '2024-05-20 07:00:00+00'),
(rt_r3_t3, round_3_inprogress_g1_c3, 'Team 3', '2024-05-20 07:00:00+00', '2024-05-20 07:00:00+00'),
(rt_r3_t4, round_3_inprogress_g1_c3, 'Team 4', '2024-05-20 07:00:00+00', '2024-05-20 07:00:00+00'),
(rt_r3_t5, round_3_inprogress_g1_c3, 'Team 5', '2024-05-20 07:00:00+00', '2024-05-20 07:00:00+00');
rt_r4_t1 := gen_random_uuid(); rt_r4_t2 := gen_random_uuid(); rt_r4_t3 := gen_random_uuid();
INSERT INTO round_teams (id, round_id, team_name_or_number, created_at, updated_at) VALUES
(rt_r4_t1, round_4_setupcomplete_g5_c1, 'Aces', '2024-05-25 10:00:00+00', '2024-05-25 10:00:00+00'),
(rt_r4_t2, round_4_setupcomplete_g5_c1, 'Kings', '2024-05-25 10:00:00+00', '2024-05-25 10:00:00+00'),
(rt_r4_t3, round_4_setupcomplete_g5_c1, 'Queens', '2024-05-25 10:00:00+00', '2024-05-25 10:00:00+00');
rt_r6_t1 := gen_random_uuid(); rt_r6_t2 := gen_random_uuid(); rt_r6_t3 := gen_random_uuid(); rt_r6_t4 := gen_random_uuid(); rt_r6_t5 := gen_random_uuid(); rt_r6_t6 := gen_random_uuid();
INSERT INTO round_teams (id, round_id, team_name_or_number, is_overall_winner, created_at, updated_at) VALUES
(rt_r6_t1, round_6_finalized_rollover_g1_c1, 'Sharks', FALSE, '2024-04-20 08:30:00+00', '2024-04-20 19:00:00+00'),
(rt_r6_t2, round_6_finalized_rollover_g1_c1, 'Minnows', FALSE, '2024-04-20 08:30:00+00', '2024-04-20 19:00:00+00'),
(rt_r6_t3, round_6_finalized_rollover_g1_c1, 'Whales', TRUE, '2024-04-20 08:30:00+00', '2024-04-20 19:00:00+00'),
(rt_r6_t4, round_6_finalized_rollover_g1_c1, 'Dolphins', FALSE, '2024-04-20 08:30:00+00', '2024-04-20 19:00:00+00'),
(rt_r6_t5, round_6_finalized_rollover_g1_c1, 'Barracudas', FALSE, '2024-04-20 08:30:00+00', '2024-04-20 19:00:00+00'),
(rt_r6_t6, round_6_finalized_rollover_g1_c1, 'Stingrays', FALSE, '2024-04-20 08:30:00+00', '2024-04-20 19:00:00+00');
rt_r7_t1 := gen_random_uuid(); rt_r7_t2 := gen_random_uuid(); rt_r7_t3 := gen_random_uuid();
INSERT INTO round_teams (id, round_id, team_name_or_number, is_overall_winner, created_at, updated_at) VALUES
(rt_r7_t1, round_7_completed_tie_g2_c3, 'Team X', FALSE, '2024-04-25 09:00:00+00', '2024-04-25 09:00:00+00'),
(rt_r7_t2, round_7_completed_tie_g2_c3, 'Team Y', FALSE, '2024-04-25 09:00:00+00', '2024-04-25 09:00:00+00'),
(rt_r7_t3, round_7_completed_tie_g2_c3, 'Team Z', FALSE, '2024-04-25 09:00:00+00', '2024-04-25 09:00:00+00');
-- Teams for Secret Agent Man's rounds
rt_r9_t1 := gen_random_uuid(); rt_r9_t2 := gen_random_uuid(); rt_r9_t3 := gen_random_uuid();
INSERT INTO round_teams (id, round_id, team_name_or_number, created_at, updated_at) VALUES
(rt_r9_t1, round_9_setupcomplete_g6_c2, 'Agents of Shield', '2024-06-02 10:00:00+00', '2024-06-02 10:00:00+00'),
(rt_r9_t2, round_9_setupcomplete_g6_c2, 'Agents of Sword', '2024-06-02 10:00:00+00', '2024-06-02 10:00:00+00'),
(rt_r9_t3, round_9_setupcomplete_g6_c2, 'Agents of Atlas', '2024-06-02 10:00:00+00', '2024-06-02 10:00:00+00');
rt_r10_t1 := gen_random_uuid(); rt_r10_t2 := gen_random_uuid(); rt_r10_t3 := gen_random_uuid();
INSERT INTO round_teams (id, round_id, team_name_or_number, created_at, updated_at) VALUES
(rt_r10_t1, round_10_inprogress_g6_c3, 'Spies Like Us', '2024-06-03 10:00:00+00', '2024-06-03 10:00:00+00'),
(rt_r10_t2, round_10_inprogress_g6_c3, 'Double Agents', '2024-06-03 10:00:00+00', '2024-06-03 10:00:00+00'),
(rt_r10_t3, round_10_inprogress_g6_c3, 'Counter Spies', '2024-06-03 10:00:00+00', '2024-06-03 10:00:00+00');

-- 8. round_participants
RAISE NOTICE 'Seeding round_participants...';
INSERT INTO round_participants (round_id, golfer_id, round_team_id, buy_in_paid) VALUES
(round_1_finalized_g1_c1, regular_golfer_r1, rt_r1_t1, TRUE), (round_1_finalized_g1_c1, regular_golfer_r2, rt_r1_t1, TRUE),
(round_1_finalized_g1_c1, regular_golfer_r3, rt_r1_t2, TRUE), (round_1_finalized_g1_c1, regular_golfer_r4, rt_r1_t2, TRUE),
(round_1_finalized_g1_c1, regular_golfer_r5, rt_r1_t3, TRUE), (round_1_finalized_g1_c1, regular_golfer_r6, rt_r1_t3, TRUE);
INSERT INTO round_participants (round_id, golfer_id, round_team_id, buy_in_paid) VALUES
(round_2_completed_g2_c2, regular_golfer_r7, rt_r2_t1, TRUE), (round_2_completed_g2_c2, regular_golfer_r8, rt_r2_t1, TRUE),
(round_2_completed_g2_c2, regular_golfer_r9, rt_r2_t2, TRUE), (round_2_completed_g2_c2, regular_golfer_r10, rt_r2_t2, TRUE),
(round_2_completed_g2_c2, regular_golfer_r11, rt_r2_t3, TRUE), (round_2_completed_g2_c2, regular_golfer_r12, rt_r2_t3, TRUE),
(round_2_completed_g2_c2, scorer_golfer_s2, rt_r2_t4, TRUE), (round_2_completed_g2_c2, scorer_golfer_s3, rt_r2_t4, TRUE);
INSERT INTO round_participants (round_id, golfer_id, round_team_id, buy_in_paid) VALUES
(round_3_inprogress_g1_c3, regular_golfer_r1, rt_r3_t1, TRUE), (round_3_inprogress_g1_c3, regular_golfer_r2, rt_r3_t1, TRUE),
(round_3_inprogress_g1_c3, regular_golfer_r3, rt_r3_t2, TRUE), (round_3_inprogress_g1_c3, regular_golfer_r4, rt_r3_t2, TRUE),
(round_3_inprogress_g1_c3, regular_golfer_r5, rt_r3_t3, TRUE), (round_3_inprogress_g1_c3, regular_golfer_r6, rt_r3_t3, TRUE),
(round_3_inprogress_g1_c3, regular_golfer_r7, rt_r3_t4, TRUE), (round_3_inprogress_g1_c3, regular_golfer_r8, rt_r3_t4, TRUE),
(round_3_inprogress_g1_c3, scorer_golfer_s1, rt_r3_t5, TRUE), (round_3_inprogress_g1_c3, admin_golfer_id_1, rt_r3_t5, TRUE);
INSERT INTO round_participants (round_id, golfer_id, round_team_id, buy_in_paid) VALUES
(round_4_setupcomplete_g5_c1, regular_golfer_r1, rt_r4_t1, TRUE), (round_4_setupcomplete_g5_c1, regular_golfer_r2, rt_r4_t1, TRUE),
(round_4_setupcomplete_g5_c1, regular_golfer_r4, rt_r4_t2, TRUE), (round_4_setupcomplete_g5_c1, regular_golfer_r5, rt_r4_t2, TRUE),
(round_4_setupcomplete_g5_c1, regular_golfer_r6, rt_r4_t3, TRUE), (round_4_setupcomplete_g5_c1, scorer_golfer_s1, rt_r4_t3, TRUE), (round_4_setupcomplete_g5_c1, admin_golfer_id_2, rt_r4_t3, TRUE);
INSERT INTO round_participants (round_id, golfer_id, round_team_id, buy_in_paid) VALUES
(round_6_finalized_rollover_g1_c1, regular_golfer_r1, rt_r6_t1, TRUE), (round_6_finalized_rollover_g1_c1, regular_golfer_r2, rt_r6_t1, TRUE),
(round_6_finalized_rollover_g1_c1, regular_golfer_r3, rt_r6_t2, TRUE), (round_6_finalized_rollover_g1_c1, regular_golfer_r4, rt_r6_t2, TRUE),
(round_6_finalized_rollover_g1_c1, regular_golfer_r5, rt_r6_t3, TRUE), (round_6_finalized_rollover_g1_c1, regular_golfer_r6, rt_r6_t3, TRUE),
(round_6_finalized_rollover_g1_c1, regular_golfer_r7, rt_r6_t4, TRUE), (round_6_finalized_rollover_g1_c1, regular_golfer_r8, rt_r6_t4, TRUE),
(round_6_finalized_rollover_g1_c1, regular_golfer_r9, rt_r6_t5, TRUE), (round_6_finalized_rollover_g1_c1, regular_golfer_r10, rt_r6_t5, TRUE),
(round_6_finalized_rollover_g1_c1, regular_golfer_r11, rt_r6_t6, TRUE), (round_6_finalized_rollover_g1_c1, regular_golfer_r12, rt_r6_t6, TRUE);
INSERT INTO round_participants (round_id, golfer_id, round_team_id, buy_in_paid) VALUES
(round_7_completed_tie_g2_c3, regular_golfer_r7, rt_r7_t1, TRUE), (round_7_completed_tie_g2_c3, regular_golfer_r8, rt_r7_t1, TRUE),
(round_7_completed_tie_g2_c3, regular_golfer_r9, rt_r7_t2, TRUE), (round_7_completed_tie_g2_c3, regular_golfer_r10, rt_r7_t2, TRUE),
(round_7_completed_tie_g2_c3, regular_golfer_r11, rt_r7_t3, TRUE), (round_7_completed_tie_g2_c3, regular_golfer_r12, rt_r7_t3, TRUE);
-- Participants for Secret Agent Man's rounds (6 players each)
INSERT INTO round_participants (round_id, golfer_id, round_team_id, buy_in_paid) VALUES
(round_9_setupcomplete_g6_c2, scorer_golfer_s6, rt_r9_t1, TRUE), (round_9_setupcomplete_g6_c2, regular_golfer_r15, rt_r9_t1, TRUE),
(round_9_setupcomplete_g6_c2, regular_golfer_r16, rt_r9_t2, TRUE), (round_9_setupcomplete_g6_c2, regular_golfer_r17, rt_r9_t2, TRUE),
(round_9_setupcomplete_g6_c2, regular_golfer_r18, rt_r9_t3, TRUE), (round_9_setupcomplete_g6_c2, regular_golfer_r19, rt_r9_t3, TRUE);
INSERT INTO round_participants (round_id, golfer_id, round_team_id, buy_in_paid) VALUES
(round_10_inprogress_g6_c3, scorer_golfer_s6, rt_r10_t1, TRUE), (round_10_inprogress_g6_c3, regular_golfer_r15, rt_r10_t1, TRUE),
(round_10_inprogress_g6_c3, regular_golfer_r16, rt_r10_t2, TRUE), (round_10_inprogress_g6_c3, regular_golfer_r17, rt_r10_t2, TRUE),
(round_10_inprogress_g6_c3, regular_golfer_r18, rt_r10_t3, TRUE), (round_10_inprogress_g6_c3, regular_golfer_r19, rt_r10_t3, TRUE);

-- 9. round_scores
RAISE NOTICE 'Seeding round_scores...';
FOR hole_idx IN 1..18 LOOP
    INSERT INTO round_scores (round_id, round_team_id, hole_number, score, is_skin_winner, skin_value_won, entered_at) VALUES
    (round_1_finalized_g1_c1, rt_r1_t1, hole_idx, get_random_score(3,4), FALSE, 0.00, '2024-05-10 17:00:00+00'),
    (round_1_finalized_g1_c1, rt_r1_t2, hole_idx, get_random_score(4,5), FALSE, 0.00, '2024-05-10 17:00:00+00'),
    (round_1_finalized_g1_c1, rt_r1_t3, hole_idx, get_random_score(4,5), FALSE, 0.00, '2024-05-10 17:00:00+00');
END LOOP;
UPDATE round_scores SET is_skin_winner = TRUE, skin_value_won = 2.00 WHERE round_id = round_1_finalized_g1_c1 AND round_team_id = rt_r1_t1 AND hole_number IN (1, 3, 5, 7, 9, 11, 13, 15, 17);
UPDATE round_scores SET score = 2 WHERE round_id = round_1_finalized_g1_c1 AND round_team_id = rt_r1_t1 AND hole_number IN (1, 3, 5, 7, 9, 11, 13, 15, 17);
UPDATE round_scores SET is_skin_winner = TRUE, skin_value_won = 2.00 WHERE round_id = round_1_finalized_g1_c1 AND round_team_id = rt_r1_t2 AND hole_number IN (2, 4, 6, 8, 10, 12, 14, 16, 18);
UPDATE round_scores SET score = 3 WHERE round_id = round_1_finalized_g1_c1 AND round_team_id = rt_r1_t2 AND hole_number IN (2, 4, 6, 8, 10, 12, 14, 16, 18);
UPDATE round_scores SET score = 3 WHERE round_id = round_1_finalized_g1_c1 AND round_team_id = rt_r1_t1 AND hole_number NOT IN (1, 3, 5, 7, 9, 11, 13, 15, 17);
UPDATE round_scores SET score = 5 WHERE round_id = round_1_finalized_g1_c1 AND round_team_id = rt_r1_t2 AND hole_number NOT IN (2, 4, 6, 8, 10, 12, 14, 16, 18);
UPDATE round_scores SET score = 5 WHERE round_id = round_1_finalized_g1_c1 AND round_team_id = rt_r1_t3;

FOR hole_idx IN 1..18 LOOP
    INSERT INTO round_scores (round_id, round_team_id, hole_number, score, entered_at) VALUES
    (round_2_completed_g2_c2, rt_r2_t1, hole_idx, get_random_score(3,5), '2024-05-15 16:30:00+00'),
    (round_2_completed_g2_c2, rt_r2_t2, hole_idx, get_random_score(3,5), '2024-05-15 16:30:00+00'),
    (round_2_completed_g2_c2, rt_r2_t3, hole_idx, get_random_score(3,5), '2024-05-15 16:30:00+00'),
    (round_2_completed_g2_c2, rt_r2_t4, hole_idx, get_random_score(3,5), '2024-05-15 16:30:00+00');
END LOOP;

FOR hole_idx IN 1..9 LOOP
    INSERT INTO round_scores (round_id, round_team_id, hole_number, score, entered_at) VALUES
    (round_3_inprogress_g1_c3, rt_r3_t1, hole_idx, get_random_score(3,6), '2024-05-20 13:00:00+00'),
    (round_3_inprogress_g1_c3, rt_r3_t2, hole_idx, get_random_score(3,6), '2024-05-20 13:00:00+00'),
    (round_3_inprogress_g1_c3, rt_r3_t3, hole_idx, get_random_score(3,6), '2024-05-20 13:00:00+00'),
    (round_3_inprogress_g1_c3, rt_r3_t4, hole_idx, get_random_score(3,6), '2024-05-20 13:00:00+00'),
    (round_3_inprogress_g1_c3, rt_r3_t5, hole_idx, get_random_score(3,6), '2024-05-20 13:00:00+00');
END LOOP;

FOR hole_idx IN 1..18 LOOP
    INSERT INTO round_scores (round_id, round_team_id, hole_number, score, is_skin_winner, skin_value_won, entered_at) VALUES
    (round_6_finalized_rollover_g1_c1, rt_r6_t1, hole_idx, get_random_score(4,5), FALSE, 0.00, '2024-04-20 18:00:00+00'),
    (round_6_finalized_rollover_g1_c1, rt_r6_t2, hole_idx, get_random_score(4,5), FALSE, 0.00, '2024-04-20 18:00:00+00'),
    (round_6_finalized_rollover_g1_c1, rt_r6_t3, hole_idx, get_random_score(3,4), FALSE, 0.00, '2024-04-20 18:00:00+00'),
    (round_6_finalized_rollover_g1_c1, rt_r6_t4, hole_idx, get_random_score(4,6), FALSE, 0.00, '2024-04-20 18:00:00+00'),
    (round_6_finalized_rollover_g1_c1, rt_r6_t5, hole_idx, get_random_score(4,6), FALSE, 0.00, '2024-04-20 18:00:00+00'),
    (round_6_finalized_rollover_g1_c1, rt_r6_t6, hole_idx, get_random_score(4,6), FALSE, 0.00, '2024-04-20 18:00:00+00');
END LOOP;
UPDATE round_scores SET score = 3 WHERE round_id = round_6_finalized_rollover_g1_c1 AND round_team_id = rt_r6_t1 AND hole_number = 1;
UPDATE round_scores SET score = 3 WHERE round_id = round_6_finalized_rollover_g1_c1 AND round_team_id = rt_r6_t2 AND hole_number = 1;
UPDATE round_scores SET score = 3 WHERE round_id = round_6_finalized_rollover_g1_c1 AND round_team_id = rt_r6_t1 AND hole_number = 2;
UPDATE round_scores SET score = 3 WHERE round_id = round_6_finalized_rollover_g1_c1 AND round_team_id = rt_r6_t2 AND hole_number = 2;
UPDATE round_scores SET score = 2, is_skin_winner = TRUE, skin_value_won = 15.00 WHERE round_id = round_6_finalized_rollover_g1_c1 AND round_team_id = rt_r6_t3 AND hole_number = 3;
UPDATE round_scores SET score = 2, is_skin_winner = TRUE, skin_value_won = 5.00 WHERE round_id = round_6_finalized_rollover_g1_c1 AND round_team_id = rt_r6_t1 AND hole_number = 7;
UPDATE round_scores SET score = 2, is_skin_winner = TRUE, skin_value_won = 5.00 WHERE round_id = round_6_finalized_rollover_g1_c1 AND round_team_id = rt_r6_t4 AND hole_number = 10;
UPDATE round_scores SET score = 2, is_skin_winner = TRUE, skin_value_won = 5.00 WHERE round_id = round_6_finalized_rollover_g1_c1 AND round_team_id = rt_r6_t5 AND hole_number = 15;
UPDATE round_scores SET score = 3 WHERE round_id = round_6_finalized_rollover_g1_c1 AND round_team_id = rt_r6_t3 AND hole_number NOT IN (3);
UPDATE round_scores SET score = 3 WHERE round_id = round_6_finalized_rollover_g1_c1 AND round_team_id IN (rt_r6_t1, rt_r6_t2) AND hole_number = 17;
UPDATE round_scores SET score = 3 WHERE round_id = round_6_finalized_rollover_g1_c1 AND round_team_id IN (rt_r6_t1, rt_r6_t2) AND hole_number = 18;
UPDATE round_scores SET is_skin_winner = FALSE, skin_value_won = 0 WHERE round_id = round_6_finalized_rollover_g1_c1 AND round_team_id = rt_r6_t3 AND hole_number IN (4,5,6,8,9,11,12,13,14,16); -- Reset these before applying correct ones
UPDATE round_scores SET is_skin_winner = FALSE, skin_value_won = 0 WHERE round_id = round_6_finalized_rollover_g1_c1 AND round_team_id = rt_r6_t1 AND hole_number IN (1,2); -- Reset these
UPDATE round_scores SET score = 2, is_skin_winner = TRUE, skin_value_won = 5.00 WHERE round_id = round_6_finalized_rollover_g1_c1 AND round_team_id = rt_r6_t3 AND hole_number IN (4,5,6,8,9,11,12,13,14,16); -- Whales win these 10 skins

FOR hole_idx IN 1..18 LOOP
    INSERT INTO round_scores (round_id, round_team_id, hole_number, score, entered_at) VALUES
    (round_7_completed_tie_g2_c3, rt_r7_t1, hole_idx, 4, '2024-04-25 17:00:00+00'),
    (round_7_completed_tie_g2_c3, rt_r7_t2, hole_idx, 4, '2024-04-25 17:00:00+00'),
    (round_7_completed_tie_g2_c3, rt_r7_t3, hole_idx, 5, '2024-04-25 17:00:00+00');
END LOOP;
UPDATE round_scores SET score = 3, is_skin_winner = TRUE, skin_value_won = 1.00 WHERE round_id = round_7_completed_tie_g2_c3 AND round_team_id = rt_r7_t1 AND hole_number = 1;
UPDATE round_scores SET score = 3, is_skin_winner = TRUE, skin_value_won = 1.00 WHERE round_id = round_7_completed_tie_g2_c3 AND round_team_id = rt_r7_t2 AND hole_number = 2;
UPDATE round_scores SET score = 3, is_skin_winner = TRUE, skin_value_won = 1.00 WHERE round_id = round_7_completed_tie_g2_c3 AND round_team_id = rt_r7_t3 AND hole_number = 3;

-- Scores for Secret Agent Man's InProgress round (Round 10) - first 3 holes
FOR hole_idx IN 1..3 LOOP
    INSERT INTO round_scores (round_id, round_team_id, hole_number, score, entered_at) VALUES
    (round_10_inprogress_g6_c3, rt_r10_t1, hole_idx, get_random_score(3,5), '2024-06-03 10:30:00+00'),
    (round_10_inprogress_g6_c3, rt_r10_t2, hole_idx, get_random_score(3,5), '2024-06-03 10:30:00+00'),
    (round_10_inprogress_g6_c3, rt_r10_t3, hole_idx, get_random_score(3,5), '2024-06-03 10:30:00+00');
END LOOP;


-- 10. round_payout_summary
RAISE NOTICE 'Seeding round_payout_summary...';
INSERT INTO round_payout_summary (id, round_id, golfer_id, team_id, total_winnings, breakdown, calculated_at) VALUES
(gen_random_uuid(), round_1_finalized_g1_c1, regular_golfer_r1, rt_r1_t1, 48.00, '{"overall": 39.00, "skins": 9.00, "cth": 0.00}'::JSONB, '2024-05-10 18:00:00+00'),
(gen_random_uuid(), round_1_finalized_g1_c1, regular_golfer_r2, rt_r1_t1, 48.00, '{"overall": 39.00, "skins": 9.00, "cth": 0.00}'::JSONB, '2024-05-10 18:00:00+00'),
(gen_random_uuid(), round_1_finalized_g1_c1, regular_golfer_r3, rt_r1_t2, 9.00, '{"overall": 0.00, "skins": 9.00, "cth": 0.00}'::JSONB, '2024-05-10 18:00:00+00'),
(gen_random_uuid(), round_1_finalized_g1_c1, regular_golfer_r4, rt_r1_t2, 9.00, '{"overall": 0.00, "skins": 9.00, "cth": 0.00}'::JSONB, '2024-05-10 18:00:00+00'),
(gen_random_uuid(), round_1_finalized_g1_c1, regular_golfer_r5, rt_r1_t3, 0.00, '{"overall": 0.00, "skins": 0.00, "cth": 0.00}'::JSONB, '2024-05-10 18:00:00+00'),
(gen_random_uuid(), round_1_finalized_g1_c1, regular_golfer_r6, rt_r1_t3, 0.00, '{"overall": 0.00, "skins": 0.00, "cth": 0.00}'::JSONB, '2024-05-10 18:00:00+00');
UPDATE round_payout_summary SET total_winnings = 51.00, breakdown = '{"overall": 39.00, "skins": 9.00, "cth": 3.00}'::JSONB
WHERE round_id = round_1_finalized_g1_c1 AND golfer_id = regular_golfer_r1;
UPDATE round_payout_summary SET total_winnings = 51.00, breakdown = '{"overall": 39.00, "skins": 9.00, "cth": 3.00}'::JSONB
WHERE round_id = round_1_finalized_g1_c1 AND golfer_id = regular_golfer_r2;

INSERT INTO round_payout_summary (id, round_id, golfer_id, team_id, total_winnings, breakdown, calculated_at) VALUES
(gen_random_uuid(), round_6_finalized_rollover_g1_c1, regular_golfer_r5, rt_r6_t3, 101.50, '{"overall": 69.00, "skins": 32.50, "cth": 0.00}'::JSONB, '2024-04-20 19:00:00+00'),
(gen_random_uuid(), round_6_finalized_rollover_g1_c1, regular_golfer_r6, rt_r6_t3, 101.50, '{"overall": 69.00, "skins": 32.50, "cth": 0.00}'::JSONB, '2024-04-20 19:00:00+00'),
(gen_random_uuid(), round_6_finalized_rollover_g1_c1, regular_golfer_r1, rt_r6_t1, 8.50, '{"overall": 0.00, "skins": 2.50, "cth": 6.00}'::JSONB, '2024-04-20 19:00:00+00'),
(gen_random_uuid(), round_6_finalized_rollover_g1_c1, regular_golfer_r2, rt_r6_t1, 8.50, '{"overall": 0.00, "skins": 2.50, "cth": 6.00}'::JSONB, '2024-04-20 19:00:00+00'),
(gen_random_uuid(), round_6_finalized_rollover_g1_c1, regular_golfer_r7, rt_r6_t4, 2.50, '{"overall": 0.00, "skins": 2.50, "cth": 0.00}'::JSONB, '2024-04-20 19:00:00+00'),
(gen_random_uuid(), round_6_finalized_rollover_g1_c1, regular_golfer_r8, rt_r6_t4, 2.50, '{"overall": 0.00, "skins": 2.50, "cth": 0.00}'::JSONB, '2024-04-20 19:00:00+00'),
(gen_random_uuid(), round_6_finalized_rollover_g1_c1, regular_golfer_r9, rt_r6_t5, 2.50, '{"overall": 0.00, "skins": 2.50, "cth": 0.00}'::JSONB, '2024-04-20 19:00:00+00'),
(gen_random_uuid(), round_6_finalized_rollover_g1_c1, regular_golfer_r10, rt_r6_t5, 2.50, '{"overall": 0.00, "skins": 2.50, "cth": 0.00}'::JSONB, '2024-04-20 19:00:00+00');
INSERT INTO round_payout_summary (id, round_id, golfer_id, team_id, total_winnings, breakdown, calculated_at)
SELECT gen_random_uuid(), round_6_finalized_rollover_g1_c1, rp.golfer_id, rp.round_team_id, 0.00, '{"overall": 0.00, "skins": 0.00, "cth": 0.00}'::JSONB, '2024-04-20 19:00:00+00'
FROM round_participants rp
WHERE rp.round_id = round_6_finalized_rollover_g1_c1
AND NOT EXISTS (SELECT 1 FROM round_payout_summary rps WHERE rps.round_id = rp.round_id AND rps.golfer_id = rp.golfer_id);

RAISE NOTICE 'Seed data insertion complete.';

END $$;