package com.sporesync

import com.sporesync.config.ConfigChanged
import com.sporesync.transfer.TransferCompleted
import com.sporesync.transfer.TransferFailed
import com.sporesync.transfer.TransferStopped
import org.slf4j.LoggerFactory
import org.springframework.beans.factory.BeanFactory
import org.springframework.context.ApplicationEvent
import org.springframework.context.ApplicationListener
import org.springframework.context.PayloadApplicationEvent
import org.springframework.context.event.SimpleApplicationEventMulticaster
import org.springframework.context.support.AbstractApplicationContext
import org.springframework.stereotype.Component

/**
 * Spring stops delivering an event at the first synchronous listener that throws. Config changes are committed
 * before they are published, and terminal transfer events complete a transfer for every consumer. One failing
 * listener must not leave the remaining consumers out of sync. Other events keep Spring's default behavior.
 */
@Component(AbstractApplicationContext.APPLICATION_EVENT_MULTICASTER_BEAN_NAME)
internal class ApplicationEventMulticaster(
    beanFactory: BeanFactory,
) : SimpleApplicationEventMulticaster(beanFactory) {
    private val log = LoggerFactory.getLogger(ApplicationEventMulticaster::class.java)

    @Suppress("TooGenericExceptionCaught")
    override fun invokeListener(
        listener: ApplicationListener<*>,
        event: ApplicationEvent,
    ) {
        val payload = (event as? PayloadApplicationEvent<*>)?.payload
        if (!isReliableEvent(payload)) {
            return super.invokeListener(
                listener,
                event,
            )
        }
        try {
            super.invokeListener(listener, event)
        } catch (e: RuntimeException) {
            log.error(
                "A {} listener failed; the remaining listeners still receive the event",
                payload?.javaClass?.simpleName ?: "event",
                e,
            )
        }
    }

    private fun isReliableEvent(payload: Any?): Boolean =
        when (payload) {
            is ConfigChanged, is TransferCompleted, is TransferFailed, is TransferStopped -> true
            else -> false
        }
}
