using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace HuLyega
{
    public interface IHuActor { } 

    public interface IHuActorMessage { } 

    public readonly struct HuActorId : IEquatable<HuActorId>
    {
        public readonly string Id { get; init; }
        public readonly string ActorName { get; init; }

        public HuActorId(object id) : this(id, null) { }

        public HuActorId(object id, string actorName)
        {
            Id = id.ToString();
            ActorName = actorName;
        }

        public readonly override bool Equals(object obj) => obj is HuActorId other && Equals(other);

        public readonly bool Equals(HuActorId other) => Id == other.Id && ActorName == other.ActorName;

        public readonly override int GetHashCode()
        {
            return HashCode.Combine(Id?.GetHashCode() ?? 0, ActorName?.GetHashCode() ?? 0);
        }

        public readonly override string ToString() => $"/huactor/{Id}/{ActorName}";

        public static bool operator ==(in HuActorId left, in HuActorId right) => left.Equals(right);

        public static bool operator !=(in HuActorId left, in HuActorId right) => !left.Equals(right);
    }

    public abstract class HuActor
    {
        protected HuActor() { }
        protected HuActor(HuActorId actorId) => ActorId = actorId;

        public HuActorId ActorId { get; init; }

        public virtual Task OnLoad() => Task.CompletedTask;      
        
        public virtual Task OnUnload() => Task.CompletedTask;
    }

    public interface IHuActorRT
    {
        void RegisterActor<T>() where T : HuActor;

        Task UnloadAsync(HuActorId actorId, CancellationToken cancellation = default);

        Task HandleMessageAsync(HuActorId actorId, IHuActorMessage actorMessage, CancellationToken cancellation = default);
    }    
}