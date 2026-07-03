using Runner.Level;
using Runner.Player;

class CameraState
{
    Pixel x;
    Pixel y;
    bool moving;
    Pixel speed;
    Pixel direction;
    Pixel movementRemainder;

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

        if (screenY > DeadZone.Bottom)
        {
            if (y < CameraBounds.MaxY)
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

    inline void StartDirection(Pixel desiredDirection)
    {
        direction = desiredDirection;
        speed = MotionSpeed.Walk;
        movementRemainder = 0;
    }

    inline void AccelerateRun()
    {
        if (speed < MotionSpeed.RunMax)
        {
            speed += MotionSpeed.RunAcceleration;
            if (speed > MotionSpeed.RunMax)
            {
                speed = MotionSpeed.RunMax;
            }
        }
    }

    inline void DecelerateToWalk()
    {
        if (speed > MotionSpeed.Walk)
        {
            speed -= MotionSpeed.Friction;
            if (speed < MotionSpeed.Walk)
            {
                speed = MotionSpeed.Walk;
            }
        }
    }

    inline void HoldDirection(bool grounded)
    {
        if (grounded)
        {
            if (Input.IsDown(Button.B))
            {
                AccelerateRun();
            }
            else
            {
                DecelerateToWalk();
            }
        }
    }

    inline void ApplyFriction()
    {
        if (speed <= MotionSpeed.Friction)
        {
            speed = 0;
            movementRemainder = 0;
            direction = Direction.None;
        }
        else
        {
            speed -= MotionSpeed.Friction;
        }
    }

    inline void UpdateIntent(Pixel desiredDirection, bool grounded)
    {
        if (desiredDirection == Direction.None)
        {
            if (grounded)
            {
                ApplyFriction();
            }
        }
        if (desiredDirection == Direction.Right)
        {
            if (direction == Direction.Right)
            {
                HoldDirection(grounded);
            }
            else
            {
                StartDirection(Direction.Right);
            }
        }
        if (desiredDirection == Direction.Left)
        {
            if (direction == Direction.Left)
            {
                HoldDirection(grounded);
            }
            else
            {
                StartDirection(Direction.Left);
            }
        }
    }

    inline void UpdateFacing(PlayerState player)
    {
        if (direction == Direction.Right)
        {
            player.displayFlipX = false;
        }
        if (direction == Direction.Left)
        {
            player.displayFlipX = true;
        }
    }

    inline void MoveRightOnePixel(PlayerState player, Pixel wallProbeY)
    {
        let screenX = ScreenX(player);
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

    inline void MoveLeftOnePixel(PlayerState player, Pixel wallProbeY)
    {
        let screenX = ScreenX(player);
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

    inline void ApplyMotionStep(PlayerState player, Pixel wallProbeY)
    {
        if (movementRemainder >= MotionSpeed.Subpixel)
        {
            movementRemainder -= MotionSpeed.Subpixel;
            if (direction == Direction.Right)
            {
                MoveRightOnePixel(player, wallProbeY);
            }
            if (direction == Direction.Left)
            {
                MoveLeftOnePixel(player, wallProbeY);
            }
        }
    }

    inline void ApplyMotion(PlayerState player, Pixel wallProbeY)
    {
        moving = false;
        if (speed != 0)
        {
            movementRemainder += speed;
            ApplyMotionStep(player, wallProbeY);
            ApplyMotionStep(player, wallProbeY);
        }
    }

    inline void HandleHorizontalInput(PlayerState player, Pixel footWorldY)
    {
        let wallProbeY = footWorldY - CollisionProbe.WallProbeHeight;
        Pixel desiredDirection = Direction.None;
        if (Input.IsDown(Button.Right))
        {
            desiredDirection = Direction.Right;
        }

        if (Input.IsDown(Button.Left))
        {
            desiredDirection = Direction.Left;
        }

        UpdateIntent(desiredDirection, player.grounded);
        UpdateFacing(player);
        ApplyMotion(player, wallProbeY);
    }
}
