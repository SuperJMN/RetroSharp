using Runner.Level;
using Runner.Player;

class CameraState
{
    Pixel x;
    Pixel y;
    bool moving;
    bool blocked;
    u8 speed;
    u8 direction;
    u8 movementRemainder;

    inline void ResetMotion()
    {
        moving = false;
        blocked = false;
        speed = 0;
        direction = Direction.None;
        movementRemainder = 0;
    }

    // A blocked pixel step only cancels the sub-pixel budget the tick had left. Wiping the
    // speed meter and the intended direction here is what made a jump that clears an
    // obstacle crawl forward at a fraction of a pixel per tick. The block is recorded
    // instead, and it stays recorded until a pixel actually moves or the player asks for a
    // different direction, so holding into a wall can never feed the speed meter.
    inline void BlockMotionStep()
    {
        blocked = true;
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

    // Airborne motion accelerates too. Freezing the speed meter at take-off leaves a jump
    // stuck on whatever fraction of a pixel per tick survived the previous ground contact,
    // which reads as a stepped, abrupt horizontal scroll while the camera also moves
    // vertically. Only the run-to-walk decay stays grounded, so releasing the run button in
    // mid-air still keeps the momentum the player already earned.
    // Pressing into a wall covers no ground, so it must not feed the speed meter at all:
    // otherwise the meter charges to RunMax against the obstacle and launches the player the
    // moment it ends. On the ground the wall bleeds the meter down to a standstill; in the
    // air it is only held, so a jump whose probe scrapes an obstacle on the way up still
    // clears it carrying the momentum it arrived with.
    inline void Accelerate(bool grounded)
    {
        if (blocked)
        {
            if (grounded)
            {
                if (speed <= MotionSpeed.Friction)
                {
                    speed = 0;
                }
                else
                {
                    speed -= MotionSpeed.Friction;
                }
            }
        }
        else if (Input.IsDown(Button.B))
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
        else if (grounded && speed > MotionSpeed.Walk)
        {
            speed -= MotionSpeed.Friction;
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
        // The block only describes the direction that hit the wall. Releasing or turning
        // away drops it immediately, so a player pinned at a standstill can always walk
        // back out instead of being held by a stale flag.
        if (desiredDirection != direction)
        {
            blocked = false;
        }

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
            blocked = false;
            player.x += 1;
            if (screenX >= DeadZone.Right)
            {
                x += 1;
            }
        }
        else
        {
            BlockMotionStep();
        }
    }

    inline void MoveLeftOnePixel(PlayerState player, Pixel wallProbeY, Pixel collisionCameraX)
    {
        let screenX = player.x - collisionCameraX;
        let leftProbeX = screenX - CollisionProbe.LeftWallProbeOffset;
        if (Camera.AabbTiles(leftProbeX, wallProbeY, Sprite.Width(mario_player), CollisionProbe.WallProbeHeight, CollisionFlag.Solid) == 0)
        {
            moving = true;
            blocked = false;
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
            BlockMotionStep();
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

    inline void CatchUpToDeadZone(PlayerState player)
    {
        // Running right from a spawn that starts ahead of the dead-zone would keep that offset
        // forever, because the camera otherwise advances exactly one pixel per moved pixel.
        if (moving && direction == Direction.Right)
        {
            if (ScreenX(player) > DeadZone.Right)
            {
                x += 1;
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
        CatchUpToDeadZone(player);
    }
}
