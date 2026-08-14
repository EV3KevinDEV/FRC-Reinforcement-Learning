using System;

namespace MoSimRL
{
    [Serializable]
    public class RlRequest
    {
        public int v;
        public int id;
        public string cmd;
        public RlRequestPayload payload;
    }

    [Serializable]
    public class RlRequestPayload
    {
        public string client;
        public int worker_id;
        public int action_dim;
        public int observation_dim;
        public float[] action;
        public int seed;
        public int frame_skip = 5;
        public int curriculum_stage;
        public string scenario;
        public string camera_name;
        public int jpeg_quality = 85;
    }

    [Serializable]
    public class RlResponse
    {
        public int v = 1;
        public int id;
        public bool ok;
        public string error;
        public RlResponsePayload payload;
    }

    [Serializable]
    public class RlResponsePayload
    {
        public string simulator;
        public string simulator_version;
        public int worker_id;
        public int team_number;
        public int action_dim;
        public int observation_dim;
        public float fixed_dt;
        public float control_dt;
        public float decision_dt;
        public int frame_skip;
        public bool virtual_camera_api;
        public bool camera_rendering_available;
        public RlCameraInfoDto[] cameras;
        public RlCameraFrameDto camera_frame;
        public RlStateDto state;
        public RlEventsDto events;
        public RlInfoDto info;
    }

    [Serializable]
    public class RlStateDto
    {
        public RlRobotDto robot = new();
        public RlMechanismDto mechanism = new();
        public RlPhysicsDto physics = new();
        public RlTaskDto task = new();
        public RlMatchDto match = new();
    }

    [Serializable]
    public class RlPhysicsDto
    {
        public float max_cage_linear_speed;
        public float max_cage_angular_speed;
    }

    [Serializable]
    public class RlRobotDto
    {
        public float[] position = new float[3];
        public float yaw_degrees;
        public float[] local_velocity = new float[3];
        public float yaw_rate;
        public float[] up = new float[3];
        public bool grounded;
        public bool enabled;
    }

    [Serializable]
    public class RlMechanismDto
    {
        public int setpoint;
        public float arm_angle;
        public float elevator_height;
        public float intake_angle;
        public float algae_arms_angle;
        public bool has_coral;
        public int coral_state;
        public bool station_mode;
    }

    [Serializable]
    public class RlTaskDto
    {
        public string phase = "seek";
        public float[] coral_relative = new float[2];
        public float coral_distance;
        public float[] coral_velocity = new float[2];
        public bool coral_valid;
        public float[] target_relative = new float[2];
        public float target_distance;
        public float active_distance;
        public float heading_error;
        public int target_level = 1;
    }

    [Serializable]
    public class RlMatchDto
    {
        public float time_remaining;
        public string game_state = "Auto";
        public float sim_time;
        public RlScoreDto score = new();
        public float score_delta;
        public bool match_complete;
    }

    [Serializable]
    public class RlScoreDto
    {
        public int coral_points;
        public int trough_points;
        public int net_points;
        public int processor_points;
        public int climb_points;
        public int park_points;
        public int leave_points;
        public int coral_scored;
        public int algae_scored;
        public int total_points;
    }

    [Serializable]
    public class RlEventsDto
    {
        public bool cycle_success;
        public bool cycle_failed;
        public bool match_complete;
    }

    [Serializable]
    public class RlInfoDto
    {
        public int worker_id;
        public int step_id;
        public int curriculum_stage;
        public string scenario;
    }

    [Serializable]
    public class RlCameraInfoDto
    {
        public string name;
        public int width;
        public int height;
        public float vertical_fov_degrees;
        public float near_clip;
        public float far_clip;
        public float[] robot_position = new float[3];
        public float[] robot_rotation_euler = new float[3];
    }

    [Serializable]
    public class RlCameraFrameDto
    {
        public string name;
        public int width;
        public int height;
        public string encoding = "jpeg";
        public string media_type = "image/jpeg";
        public string image_base64;
        public long sequence;
        public float sim_time;
    }
}
