using Runner.Level;
using Runner.Player;

class CameraState
{
    Pixel x;
    Pixel y;
    bool moving;
    u8 speed;
    u8 direction;
    u8 movementRemainder;

    inline void ResetMotion()
    {
        moving = false;
        speed = 0;
        direction = Direction.None;
        movementRemainder = 0;
    }

    inline pure Pixel ScreenX(PlayerState player) => player.x - x;
    inline pure Pixel ScreenY(PlayerState player) => player.y - y;

    inline void ApplyPosition()
    {
        Camera.SetPosition(x, y);
    }

    inline void FollowPlayer(PlayerState player)
    {
        let screenY = ScreenY(player);
        let maxScrollY = Camera.VerticalScrollMax();

        if (screenY > DeadZone.Bottom)
        {
            if (y < maxScrollY)
            {
                y += 1;
            }
        }
        else if (screenY < DeadZone.Top)
        {
            if (y > 0)
            {
                y -= 1;
            }
        }
    }

    inline void Accelerate(bool grounded)
    {
        if (grounded)
        {
            if (Input.IsDown(Button.B))
            {
                if (speed < MotionSpeed.RunMax)
                {
                    speed += MotionSpeed.Acceleration;
                }
            }
            else if (speed < MotionSpeed.Walk)
            {
                speed += MotionSpeed.Acceleration;
            }
            else if (speed > MotionSpeed.Walk)
            {
                speed -= MotionSpeed.Friction;
            }
        }
    }

    inline void ApplySkid(u8 desiredDirection, bool grounded)
    {
        if (speed <= MotionSpeed.SkidAcceleration)
        {
            if (grounded)
            {
                speed = MotionSpeed.SkidAcceleration - speed;
            }
            else
            {
                speed = MotionSpeed.SkidAcceleration;
            }
            direction = desiredDirection;
        }
        else
        {
            speed -= MotionSpeed.SkidAcceleration;
        }
    }

    inline void ApplyFriction()
    {
        if (speed <= MotionSpeed.Friction)
        {
            speed = 0;
            direction = Direction.None;
        }
        else
        {
            speed -= MotionSpeed.Friction;
        }
    }

    inline void UpdateIntent(u8 desiredDirection, bool grounded)
    {
        if (desiredDirection == Direction.None)
        {
            if (grounded)
            {
                ApplyFriction();
            }
        }
        else
        {
            if (direction == Direction.None)
            {
                direction = desiredDirection;
                if (!grounded)
                {
                    speed = MotionSpeed.Walk;
                }
            }

            if (direction == desiredDirection)
            {
                Accelerate(grounded);
            }
            else
            {
                ApplySkid(desiredDirection, grounded);
            }
        }
    }

    inline void UpdateFacing(PlayerState player, u8 desiredDirection)
    {
        if (player.grounded)
        {
            if (desiredDirection == Direction.Right)
            {
                player.displayFlipX = false;
            }
            else if (desiredDirection == Direction.Left)
            {
                player.displayFlipX = true;
            }
        }
    }

    inline void MoveRightOnePixel(PlayerState player, Pixel wallProbeY, Pixel collisionCameraX)
    {
        let screenX = player.x - collisionCameraX;
        let rightProbeX = screenX + CollisionProbe.RightWallProbeOffset;
        if (Camera.AabbTiles(rightProbeX, wallProbeY, Sprite.Width(mario_player), CollisionProbe.WallProbeHeight, CollisionFlag.Solid) == 0)
        {
            moving = true;
            player.x += 1;
            if (screenX >= DeadZone.Right)
            {
                x += 1;
            }
        }
        else
        {
            ResetMotion();
        }
    }

    inline void MoveLeftOnePixel(PlayerState player, Pixel wallProbeY, Pixel collisionCameraX)
    {
        let screenX = player.x - collisionCameraX;
        let leftProbeX = screenX - CollisionProbe.LeftWallProbeOffset;
        if (Camera.AabbTiles(leftProbeX, wallProbeY, Sprite.Width(mario_player), CollisionProbe.WallProbeHeight, CollisionFlag.Solid) == 0)
        {
            moving = true;
            player.x -= 1;
            if (screenX <= DeadZone.Left)
            {
                if (x > 0)
                {
                    x -= 1;
                }
            }
        }
        else
        {
            ResetMotion();
        }
    }

    void ApplyMotionStep(PlayerState player, Pixel wallProbeY, Pixel collisionCameraX)
    {
        if (movementRemainder >= MotionSpeed.Subpixel)
        {
            movementRemainder -= MotionSpeed.Subpixel;
            if (direction == Direction.Right)
            {
                MoveRightOnePixel(player, wallProbeY, collisionCameraX);
            }
            if (direction == Direction.Left)
            {
                MoveLeftOnePixel(player, wallProbeY, collisionCameraX);
            }
        }
    }

    inline void ApplyMotion(PlayerState player, Pixel wallProbeY)
    {
        moving = false;
        // Camera.AabbTiles still sees the runtime camera from tick start until ApplyPosition.
        let collisionCameraX = x;
        if (speed != 0)
        {
            movementRemainder += speed;
            u8 steps = 0;
            while (steps < MotionSpeed.MaxSteps)
            {
                ApplyMotionStep(player, wallProbeY, collisionCameraX);
                steps++;
            }
        }
    }

    inline void HandleHorizontalInput(PlayerState player, Pixel footWorldY)
    {
        i16 wallProbeY = footWorldY - CollisionProbe.WallProbeHeight;
        u8 desiredDirection = Direction.None;
        if (Input.IsDown(Button.Right))
        {
            desiredDirection = Direction.Right;
        }

        if (Input.IsDown(Button.Left))
        {
            desiredDirection = Direction.Left;
        }

        UpdateIntent(desiredDirection, player.grounded);
        UpdateFacing(player, desiredDirection);
        ApplyMotion(player, wallProbeY);
    }
}
